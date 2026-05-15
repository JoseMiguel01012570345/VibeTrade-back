namespace VibeTrade.Backend.Features.Policies.Dtos;

public sealed record PartySoftLeaveBody(string Reason);

/// <summary>Respuesta 200 de <c>party-soft-leave</c> (campos en camelCase vía JSON).</summary>
public sealed record PartySoftLeaveOkResponse(
    bool SkipClientTrustPenalty,
    int? OtherMemberCount,
    bool OtherMemberPenaltyApplied,
    int? TrustScoreAfterMemberPenalty);

public sealed record PartySoftLeaveArgs(
    string UserId,
    string ThreadId,
    string Reason);

/// <summary>Resultado de <see cref="VibeTrade.Backend.Features.Policies.Interfaces.IChatExitOperationsService.PartySoftLeaveAsync"/>.</summary>
public sealed record PartySoftLeaveResult(
    bool Success,
    string? ErrorCode,
    bool SkipClientTrustPenalty,
    int? OtherMemberCount = null,
    bool OtherMemberPenaltyApplied = false,
    int? TrustScoreAfterMemberPenalty = null);

/// <param name="AllowProceed">True si puede continuar el soft-leave.</param>
/// <param name="ErrorCode">
/// <c>held_payments_buyer</c>, <c>held_payments_seller_mixed</c>,
/// <c>evidence_pending</c> (evidencia enviada o rechazada con pago retenido),
/// <c>stripe_refund_failed</c>, o null.
/// </param>
/// <param name="SkipClientTrustPenalty">La penalización de confianza ya se aplicó en servidor (reembolso y/o por integrantes).</param>
/// <param name="RefundedBuyerHeldPayments">Se ejecutaron reembolsos Stripe y actualización de filas.</param>
/// <param name="RefundNoticeText">Texto completo del aviso de sistema con el detalle de pagos reembolsados; null si no aplica.</param>
/// <param name="OtherMemberCount">Integrantes contados para penalización por salida sin pagos retenidos.</param>
/// <param name="OtherMemberPenaltyApplied">Si se aplicó penalización por integrantes.</param>
/// <param name="TrustScoreAfterMemberPenalty">Confianza tras penalización por integrantes (tienda o comprador).</param>
public readonly record struct PartySoftLeavePaymentPrep(
    bool AllowProceed,
    string? ErrorCode,
    bool SkipClientTrustPenalty,
    bool RefundedBuyerHeldPayments,
    string? RefundNoticeText,
    int? OtherMemberCount = null,
    bool OtherMemberPenaltyApplied = false,
    int? TrustScoreAfterMemberPenalty = null);
