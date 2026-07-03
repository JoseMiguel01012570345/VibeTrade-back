using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Routing.Dtos;
using VibeTrade.Backend.Features.Routing.Entities;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Routing.Services;

public sealed class RouteSheetTourPlanningService(
    AppDbContext db,
    IRouteSheetRoutingMatrixService matrixService,
    ILogger<RouteSheetTourPlanningService> log) : IRouteSheetTourPlanningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(string threadId, string routeSheetId, CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();

        var calc = await UpsertCalcAsync(tid, rsid, RouteCalculationStatuses.Processing, cancellationToken)
            .ConfigureAwait(false);

        var row = await db.ChatRouteSheets
            .FirstOrDefaultAsync(r => r.ThreadId == tid && r.RouteSheetId == rsid && r.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            calc.Status = RouteCalculationStatuses.Failed;
            calc.LastError = "route_sheet_not_found";
            calc.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = RouteSheetUtils.ClonePayload(row.Payload);
        var matrix = await matrixService.BuildForRouteSheetAsync(payload, cancellationToken).ConfigureAwait(false);

        FillPerLegGeometry(payload, matrix);
        var (visitOrder, totalKm) = SolveOptimalOrder(payload, matrix);

        row.Payload = payload;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;

        calc.Status = RouteCalculationStatuses.Completed;
        calc.MatrixJson = JsonSerializer.Serialize(matrix, JsonOptions);
        calc.VisitOrderJson = visitOrder is null ? null : JsonSerializer.Serialize(visitOrder, JsonOptions);
        calc.TotalKm = totalKm;
        calc.LastError = null;
        calc.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        log.LogInformation("Ruta calculada para hoja {RouteSheetId} (km total {Km}).", rsid, totalKm);
    }

    public async Task RebuildMatrixAsync(string threadId, string routeSheetId, CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var row = await db.ChatRouteSheets
            .FirstOrDefaultAsync(r => r.ThreadId == tid && r.RouteSheetId == rsid && r.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return;

        var matrix = await matrixService.BuildForRouteSheetAsync(row.Payload, cancellationToken).ConfigureAwait(false);
        var calc = await UpsertCalcAsync(tid, rsid, RouteCalculationStatuses.Completed, cancellationToken)
            .ConfigureAwait(false);
        calc.MatrixJson = JsonSerializer.Serialize(matrix, JsonOptions);
        calc.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void FillPerLegGeometry(RouteSheetPayload payload, RouteSheetRoutingMatrixPayload matrix)
    {
        foreach (var parada in payload.Paradas ?? new List<RouteStopPayload>())
        {
            var fromKey = TryPointKey(parada.OrigenLat, parada.OrigenLng);
            var toKey = TryPointKey(parada.DestinoLat, parada.DestinoLng);
            if (fromKey is null || toKey is null)
                continue;
            if (matrix.Legs.TryGetValue(RouteSheetRoutingMatrixPayload.LegKey(fromKey, toKey), out var cell))
            {
                parada.OsrmRoadKm = Math.Round(cell.Km, 3);
                parada.OsrmRouteLatLngs = cell.PolylineLatLngs;
            }
        }
    }

    /// <summary>Orden óptimo de visita de los destinos usando OR-Tools; devuelve índices de parada y km total del tour.</summary>
    private static (List<int>? VisitOrder, double? TotalKm) SolveOptimalOrder(
        RouteSheetPayload payload,
        RouteSheetRoutingMatrixPayload matrix)
    {
        var paradas = (payload.Paradas ?? new List<RouteStopPayload>())
            .OrderBy(p => p.Orden)
            .ToList();
        if (paradas.Count == 0)
            return (null, null);

        var perLegKm = paradas.Sum(p => p.OsrmRoadKm ?? 0);

        var startKey = TryPointKey(paradas[0].OrigenLat, paradas[0].OrigenLng);
        var destKeys = paradas
            .Select(p => TryPointKey(p.DestinoLat, p.DestinoLng))
            .ToList();
        if (startKey is null || destKeys.Any(k => k is null))
            return (null, perLegKm);

        var distinctDest = destKeys.Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        if (distinctDest.Count < 2)
            return (null, perLegKm);

        // Nodos: 0 = inicio, 1..k = destinos, k+1 = sumidero.
        var nodeCount = distinctDest.Count + 2;
        var sink = nodeCount - 1;
        var big = 1_000_000_000L;
        var cost = new long[nodeCount, nodeCount];
        for (var i = 0; i < nodeCount; i++)
            for (var j = 0; j < nodeCount; j++)
                cost[i, j] = i == j ? 0 : big;

        long KmToLong(double km) => (long)Math.Round(km * 1000);

        double LookupKm(string from, string to)
        {
            if (matrix.Legs.TryGetValue(RouteSheetRoutingMatrixPayload.LegKey(from, to), out var cell))
                return cell.Km;
            return big;
        }

        for (var d = 0; d < distinctDest.Count; d++)
        {
            cost[0, d + 1] = KmToLong(LookupKm(startKey, distinctDest[d]));
            cost[d + 1, sink] = 0; // solo entregas hacia sumidero con coste bajo
            for (var e = 0; e < distinctDest.Count; e++)
            {
                if (d == e) continue;
                cost[d + 1, e + 1] = KmToLong(LookupKm(distinctDest[d], distinctDest[e]));
            }
        }
        cost[0, sink] = big;

        if (!OpenEndedTourOptimizer.TrySolve(cost, sink, out var route, out var totalCost))
            return (null, perLegKm);

        // route: nodos internos (0, destinos..., sink). Traducir a índices de parada.
        var order = new List<int>();
        foreach (var node in route)
        {
            if (node <= 0 || node >= sink)
                continue;
            var destKey = distinctDest[node - 1];
            var paradaIdx = destKeys.FindIndex(k => string.Equals(k, destKey, StringComparison.Ordinal));
            if (paradaIdx >= 0 && !order.Contains(paradaIdx))
                order.Add(paradaIdx);
        }

        var tourKm = Math.Round(totalCost / 1000.0, 3);
        return (order, tourKm > 0 ? tourKm : perLegKm);
    }

    private async Task<RouteSheetRouteCalculationRow> UpsertCalcAsync(
        string threadId,
        string routeSheetId,
        string status,
        CancellationToken cancellationToken)
    {
        var calc = await db.RouteSheetRouteCalculations
            .FirstOrDefaultAsync(c => c.ThreadId == threadId && c.RouteSheetId == routeSheetId, cancellationToken)
            .ConfigureAwait(false);
        if (calc is null)
        {
            calc = new RouteSheetRouteCalculationRow
            {
                Id = Guid.NewGuid().ToString("N"),
                ThreadId = threadId,
                RouteSheetId = routeSheetId,
            };
            db.RouteSheetRouteCalculations.Add(calc);
        }
        calc.Status = status;
        calc.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return calc;
    }

    private static string? TryPointKey(string? latRaw, string? lngRaw)
    {
        if (!double.TryParse((latRaw ?? "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lat))
            return null;
        if (!double.TryParse((lngRaw ?? "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lng))
            return null;
        return RouteSheetRoutingMatrixService.PointKey(lat, lng);
    }
}
