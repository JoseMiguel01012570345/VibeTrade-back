using MediatR;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Shared.Contracts.Events;

namespace VibeTrade.Backend.Features.Notifications.Handlers;

public sealed class AgreementSignedEventHandler(INotificationService notifications)
    : INotificationHandler<AgreementSignedEvent>
{
    public Task Handle(AgreementSignedEvent notification, CancellationToken cancellationToken)
    {
        var sellerId = (notification.SellerUserId ?? "").Trim();
        if (sellerId.Length < 2)
            return Task.CompletedTask;

        var tid = (notification.ThreadId ?? "").Trim();
        return notifications.NotifyUserAsync(
            sellerId,
            "Acuerdo firmado",
            "El comprador aceptó el acuerdo comercial.",
            tid.Length >= 4 ? tid : null,
            deepLink: tid.Length >= 4 ? $"/chat/{tid}" : null,
            cancellationToken);
    }
}
