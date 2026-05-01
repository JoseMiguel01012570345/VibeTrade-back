using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

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
    string? BuyerAvatarUrl = null,
    string? PartyExitedUserId = null,
    string? PartyExitedReason = null,
    DateTimeOffset? PartyExitedAtUtc = null);

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
    DateTimeOffset? PartyExitedAtUtc = null);

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
        OfferCommentNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Me gusta nuevo en la oferta del vendedor (no al quitar like).</summary>
    Task NotifyOfferLikeAsync(
        OfferLikeNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Me gusta nuevo en un comentario QA cuyo autor tiene cuenta.</summary>
    Task NotifyQaCommentLikeAsync(
        QaCommentLikeNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vendedor y/o suscriptor del tramo: solicitud de suscripción (publicación emergente). No notifica al comprador del hilo.
    /// <paramref name="metaJson"/> camelCase JSON con <c>routeSheetId</c>, <c>stopId</c>, <c>carrierUserId</c>.
    /// </summary>
    Task NotifyRouteTramoSubscriptionRequestAsync(
        RouteTramoSubscriptionRequestNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transportista y vendedor: confirmación de tramo (enlace al chat). Copia al vendedor opcional (p. ej. otro dispositivo).
    /// </summary>
    Task NotifyRouteTramoSubscriptionAcceptedAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Transportista: el vendedor rechazó la solicitud de tramo; <c>RouteOfferId</c> = publicación <c>emo_*</c> para <c>/offer/…</c>.</summary>
    Task NotifyRouteTramoSubscriptionRejectedAsync(
        RouteTramoSubscriptionRejectedNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Transportista: el vendedor lo expulsó de la operación (retiro de tramos, posible ajuste de confianza).</summary>
    Task NotifyRouteTramoSellerExpelledAsync(
        RouteTramoSellerExpelledNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Transportista con cuenta por teléfono cargado en la hoja: aviso de que fue indicado en un tramo (edición de hoja).</summary>
    Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Vendedor del hilo: el transportista rechazó integrarse tras figurar como contacto en la hoja.</summary>
    Task NotifyRouteSheetPreselDeclinedByCarrierAsync(
        RouteSheetPreselDeclinedByCarrierNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Vendedor: notificación in-app cuando la confianza de su tienda se reduce por hoja de ruta / expulsión (demo).</summary>
    Task NotifySellerStoreTrustPenaltyAsync(
        SellerStoreTrustPenaltyNotificationArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Participantes del hilo (<c>JoinThread</c>) y, si aplica, la ficha emergente (<c>JoinOffer</c> con <c>emo_*</c>): suscripciones de tramo actualizadas.
    /// <c>Change</c>: <c>request</c>, <c>accept</c>, <c>reject</c>, <c>withdraw</c>, edición de hoja, <c>presel_decline</c> (transportista rechaza invitación por contacto en hoja).
    /// <c>ActorUserId</c>: transportista que solicita o declina, o vendedor que acepta/rechaza solicitudes.
    /// </summary>
    Task BroadcastRouteTramoSubscriptionsChangedAsync(
        RouteTramoSubscriptionsBroadcastArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>SignalR a clientes suscritos al grupo <c>offer:{offerId}</c> (ficha abierta).</summary>
    Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        bool forceNewThread = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tras actualizar <c>OfferQaJson</c>, asegura un mensaje de chat del vendedor por cada respuesta (idempotente).
    /// </summary>
    Task SyncOfferQaAnswersForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> GetThreadIfVisibleAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprador/vendedor del hilo (reglas <see cref="T:VibeTrade.Backend.Features.Chat.Utils.ChatThreadAccess" />, método UserCanSeeThread)
    /// o transportista con suscripción <c>pending</c> o <c>confirmed</c> (no <c>rejected</c> ni <c>withdrawn</c>).
    /// </summary>
    Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default);

    /// <summary>Hilo por oferta visible para el usuario (comprador con su hilo; vendedor solo tras primer mensaje).</summary>
    Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostMessageAsync(
        PostChatMessageArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>Mensaje de acuerdo en el hilo (solo vendedor).</summary>
    Task<ChatMessageDto?> PostAgreementAnnouncementAsync(
        PostAgreementAnnouncementArgs request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aviso de sistema en el hilo (texto informativo; actor = comprador o vendedor).
    /// Requiere acceso al hilo (p. ej. expulsado sin acceso no puede publicar; comprador/vendedor con acceso sí, aunque aún no «vean» el hilo en listado por Initiator/FirstMessage).
    /// </summary>
    Task<ChatMessageDto?> PostSystemThreadNoticeAsync(
        string actorUserId,
        string threadId,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>Aviso de sistema publicado en nombre del vendedor del hilo (p. ej. acciones del transportista).</summary>
    Task<ChatMessageDto?> PostAutomatedSystemThreadNoticeAsync(
        string threadId,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>Recibo de pago con desglose y tarifa Stripe real (mensaje persistido + hub).</summary>
    Task<ChatMessageDto?> PostAutomatedPaymentFeeReceiptAsync(
        string threadId,
        ChatPaymentFeeReceiptPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de usuarios que participan en el hilo (comprador, vendedor, transportistas según las mismas reglas que el acceso al chat).
    /// </summary>
    /// <param name="threadId">Id persistido del hilo de chat.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<string>> GetThreadParticipantUserIdsAsync(
        string threadId,
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

    /// <param name="fromUtc">Inicio del rango (inclusive), UTC.</param>
    /// <param name="toUtc">Fin del rango (inclusive), UTC.</param>
    Task<IReadOnlyList<ChatNotificationDto>> ListNotificationsAsync(
        string userId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task MarkNotificationsReadAsync(string userId, IReadOnlyList<string>? notificationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Avisa a los demás participantes (grupo de usuario en SignalR) que alguien salió del chat.
    /// No requiere estar en el grupo del hilo; requiere acceso al hilo como comprador, vendedor o transportista.
    /// </summary>
    Task<bool> BroadcastParticipantLeftToOthersAsync(
        string leaverUserId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Borrado lógico del hilo y de sus mensajes (no se retornan en listados).</summary>
    Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comprador o vendedor con acuerdo aceptado: oculta el hilo solo para quien sale, guarda motivo para el resto y mantiene el hilo activo.
    /// </summary>
    Task<PartySoftLeaveResult> SoftLeaveThreadAsPartyAsync(
        PartySoftLeaveArgs request,
        CancellationToken cancellationToken = default);
}
