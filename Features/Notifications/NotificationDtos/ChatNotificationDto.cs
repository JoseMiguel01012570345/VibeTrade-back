namespace VibeTrade.Backend.Features.Notifications.NotificationDtos;

public sealed record ChatNotificationDto(
    string Id,
    string? ThreadId,
    string? MessageId,
    string? OfferId,
    string MessagePreview,
    string AuthorLabel,
    int AuthorTrustScore,
    string SenderUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc,
    string? Kind = null,
    string? MetaJson = null);
