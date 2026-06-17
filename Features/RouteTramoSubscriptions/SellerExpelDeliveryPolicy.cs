using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

/// <summary>
/// La tienda no puede expulsar al transportista mientras tiene la carga en curso
/// salvo que el tramo esté en pausa (custodia tienda).
/// </summary>
public static class SellerExpelDeliveryPolicy
{
    /// <summary>
    /// True si el transportista es titular y el tramo no está pausado ni cerrado logísticamente.
    /// </summary>
    public static bool CarrierDeliveryBlocksSellerExpel(
        string? deliveryStateRaw,
        string? currentOwnerUserId,
        string carrierUserId)
    {
        var carrierId = (carrierUserId ?? "").Trim();
        if (carrierId.Length < 2)
            return false;
        if (!string.Equals((currentOwnerUserId ?? "").Trim(), carrierId, StringComparison.Ordinal))
            return false;

        var state = (deliveryStateRaw ?? "").Trim();
        if (string.Equals(state, RouteStopDeliveryStates.EvidenceAccepted, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(state, RouteStopDeliveryStates.IdleStoreCustody, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(state, RouteStopDeliveryStates.Refunded, StringComparison.OrdinalIgnoreCase))
            return false;

        return state.Length > 0;
    }
}
