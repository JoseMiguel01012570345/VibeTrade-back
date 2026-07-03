using System.Globalization;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Features.Routing.Dtos;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Routing.Services;

public sealed class RouteSheetRoutingMatrixService(
    IDrivingLegRoutingService routing,
    ILogger<RouteSheetRoutingMatrixService> log) : IRouteSheetRoutingMatrixService
{
    /// <summary>Tope de puntos para evitar explosión de peticiones (N·(N-1) tramos dirigidos).</summary>
    private const int MaxPoints = 12;

    public async Task<RouteSheetRoutingMatrixPayload> BuildForRouteSheetAsync(
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default)
    {
        var matrix = new RouteSheetRoutingMatrixPayload();
        var points = CollectDistinctPoints(payload);
        if (points.Count == 0)
            return matrix;

        matrix.Points = points.Values
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Take(MaxPoints)
            .ToList();

        for (var i = 0; i < matrix.Points.Count; i++)
        {
            for (var j = 0; j < matrix.Points.Count; j++)
            {
                if (i == j) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var from = matrix.Points[i];
                var to = matrix.Points[j];
                var key = RouteSheetRoutingMatrixPayload.LegKey(from.Key, to.Key);
                matrix.Legs[key] = await ComputeLegAsync(from, to, cancellationToken).ConfigureAwait(false);
            }
        }

        return matrix;
    }

    private async Task<RoutingMatrixLegCell> ComputeLegAsync(
        RoutingMatrixPoint from,
        RoutingMatrixPoint to,
        CancellationToken cancellationToken)
    {
        try
        {
            var legs = await routing.GetDrivingLegsAsync(
                new[] { (from.Lat, from.Lng), (to.Lat, to.Lng) },
                cancellationToken).ConfigureAwait(false);
            if (legs is { Count: > 0 })
            {
                var leg = legs[0];
                return new RoutingMatrixLegCell
                {
                    Km = leg.DistanceKm,
                    PolylineLatLngs = leg.RouteLatLngs,
                    UsedGraphHopper = true,
                    Routable = true,
                };
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Matriz de ruta: falló tramo {From}->{To}", from.Key, to.Key);
        }

        return new RoutingMatrixLegCell
        {
            Km = HaversineKm(from.Lat, from.Lng, to.Lat, to.Lng),
            PolylineLatLngs = null,
            UsedGraphHopper = false,
            Routable = false,
        };
    }

    private static Dictionary<string, RoutingMatrixPoint> CollectDistinctPoints(RouteSheetPayload payload)
    {
        var points = new Dictionary<string, RoutingMatrixPoint>(StringComparer.Ordinal);
        foreach (var p in payload.Paradas ?? new List<RouteStopPayload>())
        {
            TryAdd(points, p.OrigenLat, p.OrigenLng, p.Origen);
            TryAdd(points, p.DestinoLat, p.DestinoLng, p.Destino);
        }
        return points;
    }

    private static void TryAdd(
        IDictionary<string, RoutingMatrixPoint> points,
        string? latRaw,
        string? lngRaw,
        string? label)
    {
        if (!TryParse(latRaw, out var lat) || !TryParse(lngRaw, out var lng))
            return;
        var key = PointKey(lat, lng);
        if (points.ContainsKey(key))
            return;
        points[key] = new RoutingMatrixPoint { Key = key, Lat = lat, Lng = lng, Label = label };
    }

    public static string PointKey(double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat:F5},{lng:F5}");

    private static bool TryParse(string? raw, out double value) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0088;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
