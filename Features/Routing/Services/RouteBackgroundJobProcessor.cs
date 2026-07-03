using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Features.Routing.Entities;
using VibeTrade.Backend.Features.Routing.Interfaces;

namespace VibeTrade.Backend.Features.Routing.Services;

/// <summary>Despacha un <see cref="RouteBackgroundJobRow"/> a su servicio según <see cref="RouteBackgroundJobRow.JobType"/>.</summary>
public sealed class RouteBackgroundJobProcessor(
    IRouteSheetTourPlanningService tourPlanning,
    ILogger<RouteBackgroundJobProcessor> log)
{
    public async Task ProcessJobAsync(RouteBackgroundJobRow job, CancellationToken ct)
    {
        switch (job.JobType)
        {
            case RouteBackgroundJobTypes.RouteSheetRouteCalculation:
            {
                var threadId = job.ThreadId
                               ?? throw new InvalidOperationException("Falta hilo correlacionado para el cálculo de ruta.");
                var routeSheetId = job.RouteSheetId
                                   ?? throw new InvalidOperationException("Falta hoja de ruta correlacionada para el cálculo de ruta.");
                await tourPlanning.ExecuteAsync(threadId, routeSheetId, ct).ConfigureAwait(false);
                break;
            }
            case RouteBackgroundJobTypes.RouteSheetMatrixRebuild:
            {
                var threadId = job.ThreadId
                               ?? throw new InvalidOperationException("Falta hilo correlacionado para reconstruir la matriz.");
                var routeSheetId = job.RouteSheetId
                                   ?? throw new InvalidOperationException("Falta hoja de ruta correlacionada para reconstruir la matriz.");
                await tourPlanning.RebuildMatrixAsync(threadId, routeSheetId, ct).ConfigureAwait(false);
                break;
            }
            default:
                log.LogWarning("Tipo de job de ruta desconocido: {Type}", job.JobType);
                break;
        }
    }
}
