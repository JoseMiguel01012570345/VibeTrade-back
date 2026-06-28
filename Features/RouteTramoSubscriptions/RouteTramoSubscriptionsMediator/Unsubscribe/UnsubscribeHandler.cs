using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.Unsubscribe;

public sealed class UnsubscribeHandler(RouteTramoSubscriptionServiceCore core)
    : IRequestHandler<UnsubscribeCommand, CarrierWithdrawFromThreadResult?>
{
    public Task<CarrierWithdrawFromThreadResult?> Handle(
        UnsubscribeCommand request,
        CancellationToken cancellationToken) =>
        core.WithdrawCarrierFromThreadAsync(
            request.CarrierUserId,
            request.ThreadId,
            request.WithdrawReason,
            request.TradeAgreementId,
            cancellationToken);
}
