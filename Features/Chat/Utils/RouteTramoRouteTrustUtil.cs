using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoRouteTrustUtil
{
    /// <summary>Ruta aún no marcada entregada (penalización de confianza al abandonar como transportista).</summary>
    public static bool IsRouteStateNotDelivered(RouteSheetPayload? payload) =>
        ((payload?.Estado ?? "").Trim().ToLowerInvariant() != "entregada");
}
