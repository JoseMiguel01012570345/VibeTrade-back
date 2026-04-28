using RouteTramoItemDto = global::VibeTrade.Backend.Features.Chat.RouteTramoSubscriptionItemDto;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoSubscriptionDtoFilter
{
    /// <summary>Visibilidad transportista: confirmados de todos + filas propias (pendientes / rechazadas / retiradas).</summary>
    public static List<RouteTramoItemDto> NarrowForCarrierViewer(
        string viewerUserId,
        List<RouteTramoItemDto> dtos)
    {
        var v = (viewerUserId ?? "").Trim();
        if (v.Length < 2)
            return [];
        return dtos
            .Where(dto =>
                string.Equals((dto.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase)
                || ChatThreadAccess.UserIdsMatchLoose(v, dto.CarrierUserId))
            .ToList();
    }
}
