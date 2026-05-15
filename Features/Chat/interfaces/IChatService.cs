using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Interfaces;

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
    string? BuyerAvatarUrl = null,
    string? PartyExitedUserId = null,
    string? PartyExitedReason = null,
    DateTimeOffset? PartyExitedAtUtc = null,
    bool IsSocialGroup = false,
    string? SocialGroupTitle = null);

public sealed record ChatThreadMemberDto(string UserId, string? DisplayName, string? AvatarUrl);

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
    string? BuyerAvatarUrl = null,
    string? PartyExitedUserId = null,
    string? PartyExitedReason = null,
    DateTimeOffset? PartyExitedAtUtc = null,
    bool IsSocialGroup = false,
    string? SocialGroupTitle = null);

public interface IChatService
{
    /// <summary>True si <paramref name="userId"/> es el dueño de la tienda del producto/servicio <paramref name="offerId"/>.</summary>
    Task<bool> IsUserSellerForOfferAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    /// <summary>Dueño de la tienda del producto/servicio, o null si no existe.</summary>
    Task<string?> GetSellerUserIdForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        bool forceNewThread = false,
        CancellationToken cancellationToken = default);

    /// <summary>Hilo de mensajería directa o grupal (sin oferta; sin acuerdos comerciales).</summary>
    Task<ChatThreadDto?> CreateSocialGroupThreadAsync(
        string creatorUserId,
        IReadOnlyList<string> otherUserIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tras actualizar <c>OfferQaJson</c>, asegura un mensaje de chat del vendedor por cada respuesta (idempotente).
    /// </summary>
    Task SyncOfferQaAnswersForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> GetThreadIfVisibleAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprador/vendedor del hilo (reglas <see cref="T:VibeTrade.Backend.Features.Chat.ChatThreadAccess" />, método UserCanSeeThread)
    /// o transportista con suscripción <c>pending</c> o <c>confirmed</c> (no <c>rejected</c> ni <c>withdrawn</c>).
    /// </summary>
    Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default);

    /// <summary>Hilo por oferta visible para el usuario (comprador con su hilo; vendedor solo tras primer mensaje).</summary>
    Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Integrantes del hilo (comprador, vendedor, transportistas con tramo activo y miembros extra de grupo social).</summary>
    Task<IReadOnlyList<ChatThreadMemberDto>?> ListSocialThreadMembersAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Solo <see cref="ChatThreadRow.InitiatorUserId"/> puede fijar el nombre del grupo.</summary>
    Task<ChatThreadDto?> PatchSocialGroupTitleAsync(
        string userId,
        string threadId,
        string? title,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostMessageAsync(
        PostChatMessageArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Actualiza entrega/lectura (solo participantes; destinatario para delivered/read).</summary>
    Task<ChatMessageDto?> UpdateMessageStatusAsync(
        UpdateChatMessageStatusArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tras el login: aplica <c>delivered</c> a mensajes de otros aún no reconocidos; el hub notifica a los emisores.
    /// </summary>
    /// <returns>Cantidad de actualizaciones aplicadas (no op si ya constaba en recibos).</returns>
    Task<int> AckAllPendingIncomingDeliveredAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>Borrado lógico del hilo y de sus mensajes (no se retornan en listados).</summary>
    Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default);
}
