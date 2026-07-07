using MediatR;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.NotificationsMediator.RouteSheetEdited;

public sealed class RouteSheetEditedEventHandler : INotificationHandler<RouteSheetEditedEvent>
{
    public Task Handle(RouteSheetEditedEvent notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
