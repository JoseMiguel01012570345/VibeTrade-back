using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications.NotificationDtos;

namespace VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

/// <summary>Envío de notificaciones de chat y eventos relacionados.</summary>
public interface INotificationService
{
    Task NotifyOfferCommentAsync(
        OfferCommentNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyOfferLikeAsync(
        OfferLikeNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyQaCommentLikeAsync(
        QaCommentLikeNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteTramoSubscriptionRequestAsync(
        RouteTramoSubscriptionRequestNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteTramoSubscriptionAcceptedAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteTramoSubscriptionRejectedAsync(
        RouteTramoSubscriptionRejectedNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteTramoSellerExpelledAsync(
        RouteTramoSellerExpelledNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteSheetPreselDeclinedByCarrierAsync(
        RouteSheetPreselDeclinedByCarrierNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteLegHandoffReadyAsync(
        RouteLegHandoffReadyNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteOwnershipGrantedAsync(
        RouteOwnershipGrantedNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifyRouteLegProximityAsync(
        RouteLegProximityNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task NotifySellerStoreTrustPenaltyAsync(
        SellerStoreTrustPenaltyNotificationArgs request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatNotificationDto>> ListNotificationsAsync(
        string userId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default);

    Task MarkNotificationsReadAsync(string userId, IReadOnlyList<string>? notificationIds, CancellationToken cancellationToken = default);

    /// <summary>Persiste fila de notificación in-app para un destinatario de mensaje (sin hub hasta SaveChanges del llamador).</summary>
    Task StageInAppNotificationForMessageRecipientAsync(
        string recipientUserId,
        ChatThreadRow thread,
        ChatMessageRow message,
        string textPreview,
        string senderUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Staging de notificaciones in-app para varios destinatarios de un mismo mensaje (sin SaveChanges).</summary>
    Task StageInAppNotificationsForMessageRecipientsAsync(
        IReadOnlyList<string> recipientUserIds,
        ChatThreadRow thread,
        ChatMessageRow message,
        string textPreview,
        string senderUserId,
        CancellationToken cancellationToken = default);

    /// <summary>La otra parte del hilo: notificación in-app y hub tras soft-leave con acuerdo.</summary>
    Task NotifyCounterpartyOfPartySoftLeaveAsync(
        ChatThreadRow thread,
        string leaverUserId,
        bool leaverIsSeller,
        string reasonTrim,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica el aviso de sistema al salir con acuerdo aceptado.
    /// Recibe <paramref name="threadSystemMessages"/> desde el llamador para evitar ciclo DI con <see cref="ChatService"/>.
    /// </summary>
    Task<bool> TryPostPartySoftLeaveSystemThreadNoticeAsync(
        IChatThreadSystemMessageService threadSystemMessages,
        string userId,
        string threadId,
        bool isSeller,
        string reasonTrim,
        CancellationToken cancellationToken = default);
}
