using MediatR;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.Handlers;

public sealed class DeliveryCompletedEventHandler(INotificationService notifications)
    : INotificationHandler<DeliveryCompletedEvent>
{
    public Task Handle(DeliveryCompletedEvent notification, CancellationToken cancellationToken)
    {
        var carrierId = (notification.CarrierUserId ?? "").Trim();
        var tid = (notification.ThreadId ?? "").Trim();
        if (carrierId.Length < 2 || tid.Length < 4)
            return Task.CompletedTask;

        return notifications.NotifyUserAsync(
            carrierId,
            "Entrega completada",
            "Se registró la finalización de un tramo de entrega en tu hoja de ruta.",
            tid,
            deepLink: $"/chat/{tid}",
            cancellationToken);
    }
}
