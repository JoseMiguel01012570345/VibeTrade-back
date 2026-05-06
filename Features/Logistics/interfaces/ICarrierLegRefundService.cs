namespace VibeTrade.Backend.Features.Logistics.Interfaces;

public interface ICarrierLegRefundService
{
    Task<(bool Ok, string? ErrorCode)> TryRefundEligibleLegAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default);
}
