using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.ListByThread;

public sealed record ListByThreadQuery(string ViewerUserId, string ThreadId)
    : IRequest<IReadOnlyList<RouteTramoSubscriptionItemDto>?>;
