namespace VibeTrade.Backend.Features.Routing;

/// <summary>Km y geometría por tramo vía motor de routing configurable (GraphHopper).</summary>
public interface IDrivingLegRoutingService
{
    /// <summary>Km por tramo (paradas consecutivas en <paramref name="positions"/>).</summary>
    Task<IReadOnlyList<double>?> GetLegDistancesKmAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default);

    /// <summary>Un tramo por cada par consecutivo en <paramref name="positions"/>; polilíneas concatenadas con deduplicación en empalmes.</summary>
    Task<IReadOnlyList<DrivingLegResult>?> GetDrivingLegsAsync(
        IReadOnlyList<(double Lat, double Lng)> positions,
        CancellationToken cancellationToken = default);
}
