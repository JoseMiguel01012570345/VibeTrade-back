namespace VibeTrade.Backend.Features.Logistics;

public sealed record CarrierTelemetryIngestResultDto(
    bool Accepted,
    string? ErrorCode,
    string? Message,
    double? ProgressFraction,
    bool OffRoute);

public sealed record RouteStopDeliveryStatusDto(
    string RouteSheetId,
    string RouteStopId,
    string State,
    string? CurrentOwnerUserId,
    double? LastTelemetryProgressFraction,
    DateTimeOffset? ProximityNotifiedAtUtc);
