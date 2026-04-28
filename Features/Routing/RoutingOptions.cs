namespace VibeTrade.Backend.Features.Routing;

/// <summary>Opciones para cálculo de rutas vía OSRM (servidor externo configurable).</summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>
    /// URL base OSRM hasta <c>/route/v1</c> (sin barra final). Ej.: <c>https://router.project-osrm.org/route/v1</c>.
    /// </summary>
    public string OsrmRouteV1BaseUrl { get; set; } =
        "https://router.project-osrm.org/route/v1";
}
