using System.Text.Json;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

public sealed record ChatThreadDto(
    string Id,
    string OfferId,
    string StoreId,
    string BuyerUserId,
    string SellerUserId,
    string InitiatorUserId,
    DateTimeOffset? FirstMessageSentAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool PurchaseMode);

public sealed record ChatMessageDto(
    string Id,
    string ThreadId,
    string SenderUserId,
    ChatTextPayload Payload,
    ChatMessageStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record ChatThreadSummaryDto(
    string Id,
    string OfferId,
    string StoreId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastMessageAtUtc,
    string? LastPreview,
    bool PurchaseMode);

public sealed record ChatNotificationDto(
    string Id,
    string ThreadId,
    string MessageId,
    string MessagePreview,
    string AuthorLabel,
    int AuthorTrustScore,
    string SenderUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public interface IChatService
{
    Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tras actualizar <c>OfferQaJson</c>, asegura un mensaje de chat del vendedor por cada respuesta (idempotente).
    /// </summary>
    Task SyncOfferQaAnswersForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> GetThreadIfVisibleAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    /// <summary>Hilo por oferta visible para el usuario (comprador con su hilo; vendedor solo tras primer mensaje).</summary>
    Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostMessageAsync(string senderUserId, string threadId, JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>Actualiza entrega/lectura (solo participantes; destinatario para delivered/read).</summary>
    Task<ChatMessageDto?> UpdateMessageStatusAsync(
        string userId,
        string threadId,
        string messageId,
        ChatMessageStatus status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatNotificationDto>> ListNotificationsAsync(string userId, CancellationToken cancellationToken = default);

    Task MarkNotificationsReadAsync(string userId, IReadOnlyList<string>? notificationIds, CancellationToken cancellationToken = default);

    /// <summary>Borrado lógico del hilo y de sus mensajes (no se retornan en listados).</summary>
    Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default);
}
