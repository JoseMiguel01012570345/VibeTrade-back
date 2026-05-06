namespace VibeTrade.Backend.Features.Logistics.Interfaces;

public interface ICarrierDeliveryEvidenceService
{
    Task<(int StatusCode, string? Error, CarrierDeliveryEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        UpsertCarrierDeliveryEvidenceRequest body,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error)> DecideAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        DecideCarrierDeliveryEvidenceRequest body,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error, CarrierDeliveryEvidenceDto? Data)> GetAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken);
}
