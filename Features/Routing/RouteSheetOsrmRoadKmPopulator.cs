using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.Routing.Interfaces;

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
        RoutingUtils.ClearOsrmFields(payload);

        var paradas = payload.Paradas ?? [];
        if (paradas.Count == 0)
            return;

        var chains = RouteSheetUtils.BuildTramoChainsByCoords(paradas);
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
        if (!RoutingUtils.TryBuildPositionsForTramoChain(paradas, chain, out var positions))
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
}
