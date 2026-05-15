using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

/// <summary>Notificaciones y broadcasts del ciclo de suscripción a tramos (chat / SignalR).</summary>
public interface IRouteTramoSubscriptionNotificationService
{
    Task NotifyLegHandoffsAfterCarrierConfirmedAsync(
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        IReadOnlyList<string> confirmedStopIds,
        CancellationToken cancellationToken = default);

    Task NotifyTramoSubscriptionAcceptedAndBroadcastAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs accepted,
        string threadId,
        string routeSheetId,
        string broadcastChange,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyTramoSubscriptionRejectedAndBroadcastAsync(
        RouteTramoSubscriptionRejectedNotificationArgs rejected,
        string threadId,
        string routeSheetId,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default);

    Task NotifySellerTrustPenaltyAfterConfirmedExpelAsync(
        SellerExpelContext ctx,
        int balanceAfter,
        CancellationToken cancellationToken = default);

    Task PublishSellerExpelledNotificationsAsync(
        SellerExpelContext ctx,
        CancellationToken cancellationToken = default);

    Task PostCarrierWithdrawSystemNoticeAndBroadcastsAsync(
        string threadId,
        string automatedSystemNoticeText,
        string broadcastActorUserId,
        IReadOnlyList<string> distinctRouteSheetIds,
        CancellationToken cancellationToken = default);

    Task NotifyPreselAcceptAndBroadcastAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs accepted,
        string threadId,
        string routeSheetId,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default);

    Task PublishPreselCarrierDeclinedAsync(
        bool sendBroadcast,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        RouteSheetPreselDeclinedByCarrierNotificationArgs declined,
        CancellationToken cancellationToken = default);
}
