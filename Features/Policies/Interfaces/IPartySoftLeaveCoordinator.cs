using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Policies.Dtos;

namespace VibeTrade.Backend.Features.Policies.Interfaces;

/// <summary>
/// Reglas de pago al abandonar el chat con acuerdo: bloqueo por pagos <c>held</c>, reembolsos y penalización al vendedor.
/// </summary>
public interface IPartySoftLeaveCoordinator
{
    /// <returns>Si <see cref="PartySoftLeavePaymentPrep.AllowProceed"/> es false, no registrar la salida.</returns>
    Task<PartySoftLeavePaymentPrep> ProcessPaymentRulesAsync(
        ChatThreadRow thread,
        bool isBuyer,
        bool isSeller,
        CancellationToken cancellationToken = default);
}
