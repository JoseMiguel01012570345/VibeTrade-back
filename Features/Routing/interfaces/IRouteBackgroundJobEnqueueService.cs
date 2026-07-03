namespace VibeTrade.Backend.Features.Routing.Interfaces;

/// <summary>Encola trabajos de cálculo de rutas (polilíneas/matrices) para el worker en background.</summary>
public interface IRouteBackgroundJobEnqueueService
{
    Task<string> EnqueueRouteSheetCalculationAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);

    Task<string> EnqueueRouteSheetMatrixRebuildAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default);
}
