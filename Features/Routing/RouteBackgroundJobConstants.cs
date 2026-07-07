namespace VibeTrade.Backend.Features.Routing;

/// <summary>Tipos de trabajo de la cola de rutas (cálculo asíncrono de polilíneas/matrices).</summary>
public static class RouteBackgroundJobTypes
{
    /// <summary>Calcular polilíneas + km por tramo y orden óptimo (OR-Tools) de una hoja de ruta.</summary>
    public const string RouteSheetRouteCalculation = "route_sheet_route_calculation";

    /// <summary>Reconstruir la matriz de distancias/polilíneas de una hoja de ruta.</summary>
    public const string RouteSheetMatrixRebuild = "route_sheet_matrix_rebuild";
}

/// <summary>Estados del trabajo en la cola.</summary>
public static class RouteBackgroundJobStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Estado del cálculo de ruta de una hoja (para que el cliente haga polling).</summary>
public static class RouteCalculationStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
