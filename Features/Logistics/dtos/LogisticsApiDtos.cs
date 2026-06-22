namespace VibeTrade.Backend.Features.Logistics.Dtos;

/// <summary>Cuerpo POST telemetría: coordenadas y metadatos; la velocidad la calcula el servidor entre muestras.</summary>
public sealed record PostTelemetryBody(
    string RouteSheetId,
    string RouteStopId,
    double Lat,
    double Lng,
    DateTimeOffset ReportedAtUtc,
    string SourceClientId);

public sealed record CedeOwnershipBody(string RouteSheetId, string RouteStopId);

public sealed record SellerPauseDeliveryBody(string RouteSheetId, string RouteStopId, string Reason);

public sealed record SellerResumeFromIdleBody(string RouteSheetId, string RouteStopId, string TargetCarrierUserId);
