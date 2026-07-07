using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.AcceptPending;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Interfaces;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.ListByThread;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.Subscribe;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.Unsubscribe;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

public sealed class RouteTramoSubscriptionService(IMediator mediator, RouteTramoSubscriptionServiceCore core)
    : IRouteTramoSubscriptionService
{
    public const string AcceptCarrierPendingConflictMessage =
        RouteTramoSubscriptionPolicy.AcceptCarrierPendingConflictMessage;

    public const string StopDeliveredSubscriptionBlockedMessage =
        RouteTramoSubscriptionServiceCore.StopDeliveredSubscriptionBlockedMessage;

    public Task RecordSubscriptionRequestAsync(
        RecordRouteTramoSubscriptionRequestArgs request,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new SubscribeCommand(request), cancellationToken);

    public Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListPublishedForThreadAsync(
        string viewerUserId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new ListByThreadQuery(viewerUserId, threadId), cancellationToken);

    public Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListForCarrierByEmergentPublicationAsync(
        string carrierUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default) =>
        core.ListForCarrierByEmergentPublicationAsync(carrierUserId, emergentOfferId, cancellationToken);

    public Task<int?> AcceptCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new AcceptPendingCommand(action), cancellationToken);

    public Task<int?> RejectCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default) =>
        core.RejectCarrierPendingOnSheetAsync(action, cancellationToken);

    public Task<CarrierWithdrawFromThreadResult?> WithdrawCarrierFromThreadAsync(
        string carrierUserId,
        string threadId,
        string withdrawReason,
        string? tradeAgreementId = null,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new UnsubscribeCommand(carrierUserId, threadId, withdrawReason, tradeAgreementId),
            cancellationToken);

    public Task<CarrierExpelledBySellerResult?> ExpelCarrierBySellerFromThreadAsync(
        string sellerUserId,
        string threadId,
        string carrierUserId,
        string reason,
        string? routeSheetId = null,
        string? stopId = null,
        CancellationToken cancellationToken = default) =>
        core.ExpelCarrierBySellerFromThreadAsync(
            sellerUserId, threadId, carrierUserId, reason, routeSheetId, stopId, cancellationToken);

    public Task<bool> CarrierRespondPreselectedRouteInviteAsync(
        CarrierPreselInviteRequest request,
        CancellationToken cancellationToken = default) =>
        core.CarrierRespondPreselectedRouteInviteAsync(request, cancellationToken);
}
