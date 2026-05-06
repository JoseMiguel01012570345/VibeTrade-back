namespace VibeTrade.Backend.Features.Logistics.Interfaces;

public interface ICarrierTelemetryService
{
    Task<CarrierTelemetryIngestResultDto?> IngestAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        double lat,
        double lng,
        double? speedKmh,
        DateTimeOffset reportedAtUtc,
        string sourceClientId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RouteStopDeliveryStatusDto>?> ListDeliveriesAsync(
        string viewerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CarrierTelemetryLatestPointDto>?> ListLatestTelemetryForRouteSheetAsync(
        string viewerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        CancellationToken cancellationToken = default);
}
