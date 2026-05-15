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
