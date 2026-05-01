namespace VibeTrade.Backend.Features.Routing;

/// <summary>Opciones para cálculo de rutas vía GraphHopper Directions API.</summary>
/// <remarks>
/// Se enlaza desde <c>appsettings.json</c> bajo <c>Routing</c> (o <c>Routing__…</c> por variables de entorno).
/// OpenAPI: https://docs.graphhopper.com/openapi
/// </remarks>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>URL base hasta <c>/api/1</c> (con o sin barra final). Ej.: <c>https://graphhopper.com/api/1</c>.</summary>
    public string GraphHopperBaseUrl { get; set; } = "https://graphhopper.com/api/1";

    /// <summary>Clave API (query <c>key</c>); obligatoria para graphhopper.com.</summary>
    public string GraphHopperApiKey { get; set; } = "";

    /// <summary>Perfil de routing (p. ej. <c>car</c>, <c>bike</c>). Parámetro <c>profile</c> en <c>/route</c>.</summary>
    public string GraphHopperProfile { get; set; } = "car";
}
