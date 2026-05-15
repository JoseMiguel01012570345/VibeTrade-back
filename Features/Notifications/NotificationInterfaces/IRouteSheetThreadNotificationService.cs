using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

/// <summary>Avisos de hilo y broadcasts relacionados con hojas de ruta: mensajes de sistema vía <see cref="IChatService"/>; notificaciones in-app y SignalR vía <c>INotificationService</c> / <c>IBroadcastingService</c>.</summary>
public interface IRouteSheetThreadNotificationService
{
    Task PostRouteSheetUpsertEditSystemNoticeAsync(
        string userId,
        string threadId,
        RouteSheetPayload? oldSnapshot,
        RouteSheetPayload persisted,
        RouteSheetEditAckPayload? nextAck,
        HashSet<string>? affectedForNotice,
        IReadOnlyList<RouteTramoSubscriptionRow>? confirmedRowsForNotice,
        CancellationToken cancellationToken = default);

    Task BroadcastRouteSheetEditPendingAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    Task NotifyAfterRouteSheetDeletedAsync(
        string userId,
        string threadId,
        string routeSheetId,
        string? sellerUserId,
        string? offerId,
        string? sheetRawTitle,
        int nConfirmedCarriers,
        int subscribedLegsCount,
        int? storeTrustBalanceAfterDelete,
        int? storeTrustDeltaDelete,
        string? emergentPublicationId,
        CancellationToken cancellationToken = default);

    Task NotifySellerStoreTrustPenaltyAfterSheetEditRejectAsync(
        string sellerUserId,
        string threadId,
        string offerId,
        int balanceAfter,
        CancellationToken cancellationToken = default);

    Task PostAutomatedSheetEditCarrierResponseNoticeAsync(
        string threadId,
        bool accepted,
        string carrierName,
        string sheetTitle,
        CancellationToken cancellationToken = default);

    Task BroadcastRouteTramoSubscriptionsSheetEditCarrierResponseAsync(
        string threadId,
        string routeSheetId,
        bool accepted,
        string carrierUserId,
        CancellationToken cancellationToken = default);

    Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default);
}
