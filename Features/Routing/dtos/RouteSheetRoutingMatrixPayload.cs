namespace VibeTrade.Backend.Features.Routing.Dtos;

/// <summary>
/// Snapshot de la matriz de rutas de una hoja: puntos ordenados + celdas dirigidas (from|to)
/// con km y polilínea. Adaptación de <c>RoutingMatrixPayload</c> de la referencia a IDs string.
/// </summary>
public sealed class RouteSheetRoutingMatrixPayload
{
    public List<RoutingMatrixPoint> Points { get; set; } = new();

    /// <summary>Clave <c>"{fromKey}|{toKey}"</c> → celda de tramo.</summary>
    public Dictionary<string, RoutingMatrixLegCell> Legs { get; set; } = new(StringComparer.Ordinal);

    public static string LegKey(string fromKey, string toKey) => $"{fromKey}|{toKey}";
}

public sealed class RoutingMatrixPoint
{
    public string Key { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string? Label { get; set; }
}

public sealed class RoutingMatrixLegCell
{
    public double Km { get; set; }

    public List<List<double>>? PolylineLatLngs { get; set; }

    /// <summary>True si vino del motor de routing (GraphHopper); false si no hubo cobertura.</summary>
    public bool UsedGraphHopper { get; set; }

    /// <summary>False: sin ruta por carretera; null: heredado.</summary>
    public bool? Routable { get; set; }
}
