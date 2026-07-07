namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Parche parcial de workspace: solo rellenar diccionarios que el cliente realmente envía.</summary>
public sealed class MarketWorkspacePatch
{
    public Dictionary<string, StoreProfileWorkspaceData>? Stores { get; set; }
    public Dictionary<string, HomeOfferViewDto>? Offers { get; set; }
    public List<string>? OfferIds { get; set; }
    public Dictionary<string, StoreCatalogBlockView>? StoreCatalogs { get; set; }
    public Dictionary<string, ChatThreadWorkspaceDto>? Threads { get; set; }
    public Dictionary<string, RouteOfferPublicEntryView>? RouteOfferPublic { get; set; }
}
