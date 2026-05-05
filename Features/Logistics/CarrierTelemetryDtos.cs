namespace VibeTrade.Backend.Features.Logistics;

public sealed record CarrierTelemetryIngestResultDto(
    bool Accepted,
    string? ErrorCode,
    string? Message,
    double? ProgressFraction,
    bool OffRoute,
    double? SpeedKmh = null,
    string? AvatarUrl = null);

public sealed record RouteStopDeliveryStatusDto(
    string RouteSheetId,
    string RouteStopId,
    string State,
    string? CurrentOwnerUserId,
    double? LastTelemetryProgressFraction,
    DateTimeOffset? ProximityNotifiedAtUtc);

/// <summary>Última muestra GPS persistida por tramo (transportista = titular actual del paquete).</summary>
public sealed record CarrierTelemetryLatestPointDto(
    string RouteSheetId,
    string RouteStopId,
    string CarrierUserId,
    double Lat,
    double Lng,
    double? ProgressFraction,
    bool OffRoute,
    DateTimeOffset ReportedAtUtc,
    double? SpeedKmh,
    string? AvatarUrl);
