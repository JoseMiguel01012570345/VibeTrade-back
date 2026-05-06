namespace VibeTrade.Backend.Features.Logistics;

public interface ICarrierOwnershipService
{
    Task<CarrierOwnershipCedeResultDto?> CedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default);
}
