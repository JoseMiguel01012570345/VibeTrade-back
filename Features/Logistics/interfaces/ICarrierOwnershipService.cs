namespace VibeTrade.Backend.Features.Logistics.Interfaces;

public interface ICarrierOwnershipService
{
    Task<CarrierOwnershipCedeResultDto?> CedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default);

    Task<CarrierOwnershipCedeResultDto?> GetCedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default);
}
