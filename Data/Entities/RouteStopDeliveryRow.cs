namespace VibeTrade.Backend.Data.Entities;

/// <summary>Estado de entrega logística por tramo (parada) de una hoja de ruta.</summary>
public static class RouteStopDeliveryStates
{
    public const string Unpaid = "unpaid";
    public const string Paid = "paid";
    public const string InTransit = "in_transit";
    public const string DeliveredPendingEvidence = "delivered_pending_evidence";
    public const string EvidenceSubmitted = "evidence_submitted";
    public const string EvidenceAccepted = "evidence_accepted";
    public const string EvidenceRejected = "evidence_rejected";
    public const string RefundedExpired = "refunded_expired";
    public const string RefundedCarrierExit = "refunded_carrier_exit";
    public const string AwaitingCarrierForHandoff = "awaiting_carrier_for_handoff";
}

/// <summary>Motivo por el cual un tramo es elegible para reembolso manual.</summary>
public static class RouteStopRefundEligibleReasons
{
    public const string EvidenceExpired = "evidence_expired";
    public const string CarrierExit = "carrier_exit";
    public const string CarrierExpelled = "carrier_expelled";
}

/// <summary>
/// Proyección runtime del ciclo de vida de un tramo pagado en el marco de un acuerdo con hoja de ruta.
/// Una fila por <c>(threadId, routeSheetId, routeStopId)</c>.
/// </summary>
public sealed class RouteStopDeliveryRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string RouteStopId { get; set; } = "";

    /// <summary><see cref="RouteStopDeliveryStates"/>.</summary>
    public string State { get; set; } = RouteStopDeliveryStates.Unpaid;

    public string? CurrentOwnerUserId { get; set; }

    public DateTimeOffset? OwnershipGrantedAtUtc { get; set; }

    public DateTimeOffset? EvidenceDeadlineAtUtc { get; set; }

    public DateTimeOffset? RefundedAtUtc { get; set; }

    /// <summary><see cref="RouteStopRefundEligibleReasons"/>.</summary>
    public string? RefundEligibleReason { get; set; }

    public DateTimeOffset? RefundEligibleSinceUtc { get; set; }

    public double? LastTelemetryProgressFraction { get; set; }

    public DateTimeOffset? ProximityNotifiedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
