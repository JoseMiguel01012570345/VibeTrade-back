namespace VibeTrade.Backend.Features.Chat.Core;

/// <summary>Delegación de notificaciones y broadcast hacia servicios dedicados (<see cref="INotificationService"/>, <see cref="IBroadcastingService"/>).</summary>
public sealed partial class ChatService
{
    public Task NotifyOfferCommentAsync(
        OfferCommentNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyOfferCommentAsync(request, cancellationToken);

    public Task NotifyOfferLikeAsync(
        OfferLikeNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyOfferLikeAsync(request, cancellationToken);

    public Task NotifyQaCommentLikeAsync(
        QaCommentLikeNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyQaCommentLikeAsync(request, cancellationToken);

    public Task NotifyRouteTramoSubscriptionRequestAsync(
        RouteTramoSubscriptionRequestNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteTramoSubscriptionRequestAsync(request, cancellationToken);

    public Task NotifyRouteTramoSubscriptionAcceptedAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteTramoSubscriptionAcceptedAsync(request, cancellationToken);

    public Task NotifyRouteTramoSubscriptionRejectedAsync(
        RouteTramoSubscriptionRejectedNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteTramoSubscriptionRejectedAsync(request, cancellationToken);

    public Task NotifyRouteTramoSellerExpelledAsync(
        RouteTramoSellerExpelledNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteTramoSellerExpelledAsync(request, cancellationToken);

    public Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteSheetPreselectedTransportistaAsync(request, cancellationToken);

    public Task NotifyRouteSheetPreselDeclinedByCarrierAsync(
        RouteSheetPreselDeclinedByCarrierNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteSheetPreselDeclinedByCarrierAsync(request, cancellationToken);

    public Task NotifyRouteLegHandoffReadyAsync(
        RouteLegHandoffReadyNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteLegHandoffReadyAsync(request, cancellationToken);

    public Task NotifyRouteOwnershipGrantedAsync(
        RouteOwnershipGrantedNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteOwnershipGrantedAsync(request, cancellationToken);

    public Task NotifyRouteLegProximityAsync(
        RouteLegProximityNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteLegProximityAsync(request, cancellationToken);

    public Task BroadcastCarrierTelemetryUpdatedAsync(
        string threadId,
        string routeSheetId,
        string agreementId,
        string routeStopId,
        string carrierUserId,
        double lat,
        double lng,
        double? progressFraction,
        bool offRoute,
        DateTimeOffset reportedAtUtc,
        double? speedKmh,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
        => broadcasting.BroadcastCarrierTelemetryUpdatedAsync(
            threadId,
            routeSheetId,
            agreementId,
            routeStopId,
            carrierUserId,
            lat,
            lng,
            progressFraction,
            offRoute,
            reportedAtUtc,
            speedKmh,
            avatarUrl,
            cancellationToken);

    public Task NotifySellerStoreTrustPenaltyAsync(
        SellerStoreTrustPenaltyNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifySellerStoreTrustPenaltyAsync(request, cancellationToken);

    public Task BroadcastRouteTramoSubscriptionsChangedAsync(
        RouteTramoSubscriptionsBroadcastArgs request,
        CancellationToken cancellationToken = default)
        => broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(request, cancellationToken);

    public Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default)
        => broadcasting.BroadcastOfferCommentsUpdatedAsync(offerId, cancellationToken);

    public Task<IReadOnlyList<ChatNotificationDto>> ListNotificationsAsync(
        string userId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
        => notifications.ListNotificationsAsync(userId, fromUtc, toUtc, cancellationToken);

    public Task MarkNotificationsReadAsync(
        string userId,
        IReadOnlyList<string>? notificationIds,
        CancellationToken cancellationToken = default)
        => notifications.MarkNotificationsReadAsync(userId, notificationIds, cancellationToken);

    public Task<IReadOnlyList<string>> GetThreadParticipantUserIdsAsync(
        string threadId,
        CancellationToken cancellationToken = default)
        => broadcasting.GetThreadParticipantUserIdsAsync(threadId, cancellationToken);

    public Task<bool> BroadcastParticipantLeftToOthersAsync(
        string leaverUserId,
        string threadId,
        CancellationToken cancellationToken = default)
        => broadcasting.BroadcastParticipantLeftToOthersAsync(leaverUserId, threadId, cancellationToken);
}
