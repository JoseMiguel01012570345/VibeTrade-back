namespace VibeTrade.Backend.Features.Routing;

/// <summary>Km y puntos [lat, lng] por tramo (motor de routing externo, p. ej. GraphHopper).</summary>
public sealed record DrivingLegResult(double DistanceKm, List<List<double>>? RouteLatLngs);
