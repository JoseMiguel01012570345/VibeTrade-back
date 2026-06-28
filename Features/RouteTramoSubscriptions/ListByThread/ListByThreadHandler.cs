using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.ListByThread;

public sealed class ListByThreadHandler(RouteTramoSubscriptionServiceCore core)
    : IRequestHandler<ListByThreadQuery, IReadOnlyList<RouteTramoSubscriptionItemDto>?>
{
    public Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> Handle(
        ListByThreadQuery request,
        CancellationToken cancellationToken) =>
        core.ListPublishedForThreadAsync(request.ViewerUserId, request.ThreadId, cancellationToken);
}
