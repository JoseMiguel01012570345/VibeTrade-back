using VibeTrade.Backend.Features.Routing.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Routing.Interfaces;

/// <summary>Construye la matriz de distancias/polilíneas entre los puntos de una hoja de ruta.</summary>
public interface IRouteSheetRoutingMatrixService
{
    Task<RouteSheetRoutingMatrixPayload> BuildForRouteSheetAsync(
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default);
}
