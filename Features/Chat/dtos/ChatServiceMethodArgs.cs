namespace VibeTrade.Backend.Features.Chat.Dtos;

public sealed record PostChatMessageArgs(
    string SenderUserId,
    string ThreadId,
    PostChatMessageBody Message);

public sealed record PostAgreementAnnouncementArgs(
    string SellerUserId,
    string ThreadId,
    string AgreementId,
    string Title,
    string Status);

public sealed record UpdateChatMessageStatusArgs(
    string UserId,
    string ThreadId,
    string MessageId,
    ChatMessageStatus Status);

public sealed record PartySoftLeaveArgs(
    string UserId,
    string ThreadId,
    string Reason);

/// <summary>Resultado de <see cref="VibeTrade.Backend.Features.Policies.ChatExit.IChatExitOperationsService.PartySoftLeaveAsync"/>.</summary>
public sealed record PartySoftLeaveResult(
    bool Success,
    string? ErrorCode,
    bool SkipClientTrustPenalty,
    int? OtherMemberCount = null,
    bool OtherMemberPenaltyApplied = false,
    int? TrustScoreAfterMemberPenalty = null);
