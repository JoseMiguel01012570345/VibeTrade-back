using System.Text.Json.Serialization;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Ficha de tienda en <c>workspace.stores[id]</c>.</summary>
public sealed class StoreProfileWorkspaceData
{
    public string? Id { get; set; }
    public string? OwnerUserId { get; set; }
    public string? Name { get; set; }
    public bool? Verified { get; set; }
    public IReadOnlyList<string>? Categories { get; set; }
    public bool? TransportIncluded { get; set; }
    public int? TrustScore { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Pitch { get; set; }
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("location")]
    public StoreLocationPointBody? Location { get; set; }
}

public sealed record StoreLocationPointBody
{
    public double Lat { get; init; }
    public double Lng { get; init; }
}

public sealed class RouteOfferPublicEntryView
{
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; set; }
}

/// <summary>Raíz de <c>market_workspaces.payload</c>.</summary>
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
