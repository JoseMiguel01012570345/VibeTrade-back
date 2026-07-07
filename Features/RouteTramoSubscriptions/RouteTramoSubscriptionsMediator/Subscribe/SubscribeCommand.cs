using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.Subscribe;

public sealed record SubscribeCommand(RecordRouteTramoSubscriptionRequestArgs Request) : IRequest<Unit>;
