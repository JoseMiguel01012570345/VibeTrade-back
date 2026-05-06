using System.Text.Json.Serialization;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;

/// <summary>Raíz de <c>market_workspaces.payload</c> (y <c>market</c> en bootstrap). Sin <c>JsonObject</c>.</summary>
public sealed class MarketWorkspaceState
{
    public Dictionary<string, StoreProfileWorkspaceData> Stores { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, HomeOfferViewDto> Offers { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("offerIds")]
    public List<string> OfferIds { get; set; } = new();

    [JsonPropertyName("storeCatalogs")]
    public Dictionary<string, StoreCatalogBlockView> StoreCatalogs { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ChatThreadWorkspaceDto> Threads { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("routeOfferPublic")]
    public Dictionary<string, RouteOfferPublicEntryView> RouteOfferPublic { get; set; } = new(StringComparer.Ordinal);
}

public sealed class RouteOfferPublicEntryView
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; set; }
}
