using MediatR;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.Subscribe;

public sealed class SubscribeHandler(RouteTramoSubscriptionServiceCore core)
    : IRequestHandler<SubscribeCommand, Unit>
{
    public async Task<Unit> Handle(SubscribeCommand request, CancellationToken cancellationToken)
    {
        await core.RecordSubscriptionRequestAsync(request.Request, cancellationToken);
        return Unit.Value;
    }
}
