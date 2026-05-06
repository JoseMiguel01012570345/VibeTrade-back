using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>Snapshot del mercado (jsonb: columna <c>Payload</c>).</summary>
public sealed class MarketWorkspaceRow
{
    public int Id { get; set; }

    public MarketWorkspaceState State { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }
}
