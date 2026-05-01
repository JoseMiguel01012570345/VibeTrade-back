using System.Globalization;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Routing;

/// <summary>
/// Rellena <see cref="RouteStopPayload.OsrmRoadKm"/> y <see cref="RouteStopPayload.OsrmRouteLatLngs"/> al guardar la hoja:
/// GraphHopper <c>/route</c>, una petición por tramo consecutivo en la cadena; empalmes deduplicados al concatenar.
/// </summary>
public static class RouteSheetOsrmRoadKmPopulator
{
    public static async Task ApplyAsync(
        RouteSheetPayload payload,
        IDrivingLegRoutingService routing,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var p in payload.Paradas)
        {
            p.OsrmRoadKm = null;
            p.OsrmRouteLatLngs = null;
        }

        var paradas = payload.Paradas;
        if (paradas.Count == 0)
            return;

        var chains = RouteSheetPayloadValidator.GetTramoChainsInParadasListOrder(paradas);
        foreach (var chain in chains)
        {
            if (chain.Count == 0)
                continue;

            try
            {
                await FillChainAsync(paradas, chain, routing, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GraphHopper road km: falló cadena de {Count} tramo(s).", chain.Count);
            }
        }
    }

    private static async Task FillChainAsync(
        List<RouteStopPayload> paradas,
        List<int> chain,
        IDrivingLegRoutingService routing,
        CancellationToken cancellationToken)
    {
        var positions = new List<(double Lat, double Lng)>();
        var first = paradas[chain[0]];
        if (!TryParseCoord(first.OrigenLat, first.OrigenLng, out var oLat, out var oLng))
            return;
        positions.Add((oLat, oLng));

        foreach (var idx in chain)
        {
            var stop = paradas[idx];
            if (!TryParseCoord(stop.DestinoLat, stop.DestinoLng, out var dLat, out var dLng))
                return;
            positions.Add((dLat, dLng));
        }

        if (positions.Count < 2)
            return;

        var legs = await routing.GetDrivingLegsAsync(positions, cancellationToken);
        if (legs is null || legs.Count != chain.Count)
            return;

        for (var i = 0; i < chain.Count; i++)
        {
            var leg = legs[i];
            paradas[chain[i]].OsrmRoadKm = leg.DistanceKm;
            paradas[chain[i]].OsrmRouteLatLngs = leg.RouteLatLngs;
        }
    }

    private static bool TryParseCoord(string? latRaw, string? lngRaw, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var ls = (latRaw ?? "").Trim();
        var gs = (lngRaw ?? "").Trim();
        if (ls.Length == 0 || gs.Length == 0)
            return false;
        return double.TryParse(ls, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(gs, CultureInfo.InvariantCulture, out lng)
            && lat is >= -90 and <= 90
            && lng is >= -180 and <= 180;
    }
}
