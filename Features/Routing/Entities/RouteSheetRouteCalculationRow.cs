namespace VibeTrade.Backend.Features.Routing.Entities;

/// <summary>
/// Estado + resultado del cálculo de ruta de una hoja: matriz de distancias/polilíneas (snapshot JSON),
/// orden óptimo (OR-Tools) y km total. Una fila por <c>(ThreadId, RouteSheetId)</c>.
/// </summary>
public sealed class RouteSheetRouteCalculationRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    /// <summary><see cref="RouteCalculationStatuses"/>.</summary>
    public string Status { get; set; } = RouteCalculationStatuses.Pending;

    /// <summary>Snapshot de la matriz de rutas (JSON de <c>RouteSheetRoutingMatrixPayload</c>).</summary>
    public string? MatrixJson { get; set; }

    /// <summary>Orden óptimo de visita (JSON: lista de índices de parada).</summary>
    public string? VisitOrderJson { get; set; }

    public double? TotalKm { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
