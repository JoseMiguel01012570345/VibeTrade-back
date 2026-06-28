using MediatR;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.Handlers;

public sealed class UserNotificationRequestedEventHandler(INotificationService notifications)
    : INotificationHandler<UserNotificationRequestedEvent>
{
    public Task Handle(UserNotificationRequestedEvent notification, CancellationToken cancellationToken) =>
        notifications.NotifyUserAsync(
            notification.UserId,
            notification.Title,
            notification.Body,
            notification.ThreadId,
            notification.DeepLink,
            cancellationToken);
}
