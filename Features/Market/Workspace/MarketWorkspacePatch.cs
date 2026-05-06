namespace VibeTrade.Backend.Features.Market.Workspace;

/// <summary>Parche parcial de workspace: solo rellenar diccionarios que el cliente realmente envía.</summary>
public sealed class MarketWorkspacePatch
{
    public Dictionary<string, StoreProfileWorkspaceData>? Stores { get; set; }
    public Dictionary<string, HomeOfferViewDto>? Offers { get; set; }
    public List<string>? OfferIds { get; set; }
    public Dictionary<string, StoreCatalogBlockView>? StoreCatalogs { get; set; }
    public Dictionary<string, VibeTrade.Backend.Features.Chat.ChatThreadWorkspaceDto>? Threads { get; set; }
    public Dictionary<string, RouteOfferPublicEntryView>? RouteOfferPublic { get; set; }

    public static MarketWorkspaceState Merge(MarketWorkspaceState existing, MarketWorkspacePatch patch)
    {
        if (patch.Stores is not null)
        {
            foreach (var kv in patch.Stores)
                existing.Stores[kv.Key] = kv.Value;
        }

        if (patch.Offers is not null)
        {
            foreach (var kv in patch.Offers)
                existing.Offers[kv.Key] = kv.Value;
        }

        if (patch.OfferIds is not null)
            existing.OfferIds = new List<string>(patch.OfferIds);

        if (patch.StoreCatalogs is not null)
        {
            foreach (var kv in patch.StoreCatalogs)
                existing.StoreCatalogs[kv.Key] = kv.Value;
        }

        if (patch.Threads is not null)
        {
            foreach (var kv in patch.Threads)
                existing.Threads[kv.Key] = kv.Value;
        }

        if (patch.RouteOfferPublic is not null)
        {
            foreach (var kv in patch.RouteOfferPublic)
                existing.RouteOfferPublic[kv.Key] = kv.Value;
        }

        return existing;
    }
}
