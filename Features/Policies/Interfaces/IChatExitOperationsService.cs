using VibeTrade.Backend.Features.Policies.Dtos;

namespace VibeTrade.Backend.Features.Policies.Interfaces;

/// <summary>Orquesta salida del chat (p. ej. soft-leave de parte); avisos de sistema en el hilo vía <see cref="VibeTrade.Backend.Features.Notifications.NotificationInterfaces.IChatThreadSystemMessageService"/>.</summary>
public interface IChatExitOperationsService
{
    Task<PartySoftLeaveResult> PartySoftLeaveAsync(
        PartySoftLeaveArgs args,
        CancellationToken cancellationToken = default);
}
