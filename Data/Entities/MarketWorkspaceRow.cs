namespace VibeTrade.Backend.Data.Entities;

/// <summary>Snapshot serializable del mercado (alineado al store Zustand del web).</summary>
public sealed class MarketWorkspaceRow
{
    public int Id { get; set; }
    public string Payload { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}
