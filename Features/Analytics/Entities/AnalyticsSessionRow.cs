namespace VibeTrade.Backend.Features.Analytics.Entities;

/// <summary>Sesión anónima de analítica (clave por pestaña del navegador).</summary>
public sealed class AnalyticsSessionRow
{
    public string Id { get; set; } = "";

    /// <summary>Clave anónima única generada por el cliente (sessionStorage).</summary>
    public string SessionKey { get; set; } = "";

    public string IpAddress { get; set; } = "";

    public string? UserAgent { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
