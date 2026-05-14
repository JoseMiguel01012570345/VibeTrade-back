using VibeTrade.Backend.Features.Chat.Core;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <summary>Orquesta salida del chat (p. ej. soft-leave de parte) y retiros de transportista; mensajes vía <see cref="IMessageHandlingService"/>.</summary>
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
