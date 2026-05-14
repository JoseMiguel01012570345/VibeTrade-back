namespace VibeTrade.Backend.Features.Policies.Dtos;

public sealed record PartySoftLeaveBody(string Reason);

/// <summary>Respuesta 200 de <c>party-soft-leave</c> (campos en camelCase vía JSON).</summary>
public sealed record PartySoftLeaveOkResponse(
    bool SkipClientTrustPenalty,
    int? OtherMemberCount,
    bool OtherMemberPenaltyApplied,
    int? TrustScoreAfterMemberPenalty);
