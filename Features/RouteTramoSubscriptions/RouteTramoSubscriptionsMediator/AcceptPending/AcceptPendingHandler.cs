using MediatR;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.AcceptPending;

public sealed class AcceptPendingHandler(RouteTramoSubscriptionServiceCore core)
    : IRequestHandler<AcceptPendingCommand, int?>
{
    public Task<int?> Handle(AcceptPendingCommand request, CancellationToken cancellationToken) =>
        core.AcceptCarrierPendingOnSheetAsync(request.Action, cancellationToken);
}
