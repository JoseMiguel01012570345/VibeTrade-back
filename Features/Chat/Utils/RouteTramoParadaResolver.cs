using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoParadaResolver
{
    public static RouteStopPayload? FindByStopIdOrOrden(
        IReadOnlyList<RouteStopPayload> paradas,
        string? stopId,
        int stopOrden)
    {
        var sid = (stopId ?? "").Trim();
        if (sid.Length > 0)
        {
            var byId = paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }
        if (stopOrden > 0)
            return paradas.FirstOrDefault(p => p.Orden == stopOrden);
        return null;
    }
}
