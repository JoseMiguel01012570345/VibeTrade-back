namespace VibeTrade.Backend.Features.Analytics.Entities;

/// <summary>Evento de vista de ficha de producto (para métricas de interés por producto).</summary>
public sealed class ProductViewEventRow
{
    public string Id { get; set; } = "";

    public string ProductId { get; set; } = "";

    public string SessionKey { get; set; } = "";

    public string IpAddress { get; set; } = "";

    public DateTimeOffset ViewedAt { get; set; }
}
