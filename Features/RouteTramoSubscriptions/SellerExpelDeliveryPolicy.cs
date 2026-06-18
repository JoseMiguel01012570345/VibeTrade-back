using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

/// <summary>
/// La tienda no puede expulsar al transportista mientras tiene la carga en curso
/// salvo que el tramo esté en pausa (custodia tienda). Con evidencia aceptada puede expulsar sin penalización;
/// con evidencia pendiente o rechazada no puede expulsar.
/// </summary>
public static class SellerExpelDeliveryPolicy
{
    /// <summary>
    /// Código de error de expulsión por estado de evidencia del tramo, o null si no bloquea.
    /// </summary>
    public static string? SellerExpelBlockedByEvidenceState(string? deliveryStateRaw)
    {
        var state = (deliveryStateRaw ?? "").Trim();
        if (string.Equals(state, RouteStopDeliveryStates.EvidenceRejected, StringComparison.OrdinalIgnoreCase))
            return "seller_expel_evidence_rejected";
        if (string.Equals(state, RouteStopDeliveryStates.DeliveredPendingEvidence, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, RouteStopDeliveryStates.EvidenceSubmitted, StringComparison.OrdinalIgnoreCase))
            return "seller_expel_evidence_pending";
        return null;
    }

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

        var state = (deliveryStateRaw ?? "").Trim();
        if (string.Equals(state, RouteStopDeliveryStates.EvidenceAccepted, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals((currentOwnerUserId ?? "").Trim(), carrierId, StringComparison.Ordinal))
            return false;
        if (string.Equals(state, RouteStopDeliveryStates.IdleStoreCustody, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(state, RouteStopDeliveryStates.Refunded, StringComparison.OrdinalIgnoreCase))
            return false;

        return state.Length > 0;
    }

    /// <summary>True si la expulsión de un tramo confirmado no debe penalizar la confianza de la tienda.</summary>
    public static bool ConfirmedStopSellerExpelWithoutTrustPenalty(string? deliveryStateRaw) =>
        string.Equals(
            (deliveryStateRaw ?? "").Trim(),
            RouteStopDeliveryStates.EvidenceAccepted,
            StringComparison.OrdinalIgnoreCase);
}
