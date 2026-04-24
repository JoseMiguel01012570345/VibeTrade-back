namespace VibeTrade.Backend.Features.Routing;

/// <summary>Km por tramo vía OSRM (proxy desde el backend).</summary>
public interface IOsrmLegDistanceService
{
    /// <summary>Km por tramo (petición OSRM sin geometría).</summary>
    Task<IReadOnlyList<double>?> GetLegDistancesKmAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default);
}
