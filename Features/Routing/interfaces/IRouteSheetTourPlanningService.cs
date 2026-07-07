namespace VibeTrade.Backend.Features.Routing.Interfaces;

/// <summary>Ejecuta el cálculo de ruta de una hoja: matriz + OR-Tools + polilíneas por tramo + estado.</summary>
public interface IRouteSheetTourPlanningService
{
    Task ExecuteAsync(string threadId, string routeSheetId, CancellationToken cancellationToken = default);

    Task RebuildMatrixAsync(string threadId, string routeSheetId, CancellationToken cancellationToken = default);
}
