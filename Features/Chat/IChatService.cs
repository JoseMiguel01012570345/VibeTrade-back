using System.Text.Json;
using JsonElement = System.Text.Json.JsonElement;
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
    bool PurchaseMode,
    string? BuyerDisplayName = null,
    string? BuyerAvatarUrl = null);

public sealed record ChatMessageDto(
    string Id,
    string ThreadId,
    string SenderUserId,
    ChatMessagePayload Payload,
    ChatMessageStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? SenderDisplayLabel = null);

public sealed record ChatThreadSummaryDto(
    string Id,
    string OfferId,
    string StoreId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastMessageAtUtc,
    string? LastPreview,
    bool PurchaseMode,
    string BuyerUserId,
    string SellerUserId,
    string? BuyerDisplayName = null,
    string? BuyerAvatarUrl = null);

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

public interface IChatService
{
    /// <summary>True si <paramref name="userId"/> es el dueño de la tienda del producto/servicio <paramref name="offerId"/>.</summary>
    Task<bool> IsUserSellerForOfferAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    /// <summary>Dueño de la tienda del producto/servicio, o null si no existe.</summary>
    Task<string?> GetSellerUserIdForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    /// <summary>Notificación por comentario público en la ficha de oferta (sin hilo de chat).</summary>
    Task NotifyOfferCommentAsync(
        string recipientUserId,
        string offerId,
        string textPreview,
        string authorLabel,
        int authorTrust,
        string senderUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Me gusta nuevo en la oferta del vendedor (no al quitar like).</summary>
    Task NotifyOfferLikeAsync(
        string sellerUserId,
        string offerId,
        string likerLabel,
        int likerTrust,
        string likerSenderUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Me gusta nuevo en un comentario QA cuyo autor tiene cuenta.</summary>
    Task NotifyQaCommentLikeAsync(
        string commentAuthorUserId,
        string offerId,
        string likerLabel,
        int likerTrust,
        string likerSenderUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprador y vendedor del hilo: solicitud de suscripción a tramo (publicación emergente).
    /// <paramref name="metaJson"/> camelCase JSON con <c>routeSheetId</c>, <c>stopId</c>, <c>carrierUserId</c>.
    /// </summary>
    Task NotifyRouteTramoSubscriptionRequestAsync(
        IReadOnlyCollection<string> recipientUserIds,
        string threadId,
        string messagePreview,
        string authorLabel,
        int authorTrust,
        string carrierUserId,
        string? metaJson,
        CancellationToken cancellationToken = default);

    /// <summary>SignalR a clientes suscritos al grupo <c>offer:{offerId}</c> (ficha abierta).</summary>
    Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default);

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

    /// <summary>Mensaje de acuerdo en el hilo (solo vendedor).</summary>
    Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string title,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>Aviso de sistema en el hilo (texto informativo; actor = comprador o vendedor).</summary>
    Task<ChatMessageDto?> PostSystemThreadNoticeAsync(
        string actorUserId,
        string threadId,
        string text,
        CancellationToken cancellationToken = default);

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
