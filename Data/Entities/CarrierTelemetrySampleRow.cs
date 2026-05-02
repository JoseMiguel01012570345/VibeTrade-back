namespace VibeTrade.Backend.Data.Entities;

/// <summary>Muestra append-only de telemetría GPS del transportista con ownership activo.</summary>
public sealed class CarrierTelemetrySampleRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string RouteStopId { get; set; } = "";

    public string CarrierUserId { get; set; } = "";

    public double Lat { get; set; }

    public double Lng { get; set; }

    /// <summary>Velocidad en km/h según el cliente (p. ej. Geolocation API).</summary>
    public double? SpeedKmh { get; set; }

    /// <summary>Momento reportado por el cliente (UTC).</summary>
    public DateTimeOffset ReportedAtUtc { get; set; }

    public DateTimeOffset ServerReceivedAtUtc { get; set; }

    /// <summary>Identificador opaco del cliente (<c>web-…</c>, <c>flutter-…</c>).</summary>
    public string SourceClientId { get; set; } = "";

    public double? ProgressFraction { get; set; }

    public bool OffRoute { get; set; }
}
