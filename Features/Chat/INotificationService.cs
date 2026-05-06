namespace VibeTrade.Backend.Features.Chat;

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
}
