namespace VibeTrade.Backend.Features.Analytics.Entities;

/// <summary>Evento de vista de página (tráfico) anónimo.</summary>
public sealed class AnalyticsPageViewRow
{
    public string Id { get; set; } = "";

    public string SessionKey { get; set; } = "";

    public string IpAddress { get; set; } = "";

    public string Path { get; set; } = "";

    public DateTimeOffset ViewedAt { get; set; }
}
