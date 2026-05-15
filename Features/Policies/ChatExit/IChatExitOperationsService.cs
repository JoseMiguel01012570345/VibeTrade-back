using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <summary>Orquesta salida del chat (p. ej. soft-leave de parte) y retiros de transportista; avisos de sistema en el hilo vía <see cref="IChatThreadSystemMessageService"/>.</summary>
public interface IChatExitOperationsService
{
    Task<PartySoftLeaveResult> PartySoftLeaveAsync(
        PartySoftLeaveArgs args,
        CancellationToken cancellationToken = default);

    Task<CarrierWithdrawFromThreadResult?> CarrierWithdrawAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);
}
