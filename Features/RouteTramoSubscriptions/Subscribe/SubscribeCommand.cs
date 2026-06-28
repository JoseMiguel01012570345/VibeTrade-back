using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.Subscribe;

public sealed record SubscribeCommand(RecordRouteTramoSubscriptionRequestArgs Request) : IRequest<Unit>;
