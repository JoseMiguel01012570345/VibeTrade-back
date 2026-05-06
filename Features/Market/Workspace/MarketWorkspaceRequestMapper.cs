namespace VibeTrade.Backend.Features.Market.Workspace;

public static class MarketWorkspaceRequestMapper
{
    public static MarketWorkspacePatch ToStoreProfilesPatch(WorkspaceStorePutRequest body)
    {
        if (body.Stores is { Count: > 0 } byId)
            return new MarketWorkspacePatch { Stores = new Dictionary<string, StoreProfileWorkspaceData>(byId, StringComparer.Ordinal) };

        var id = (body.Id ?? "").Trim();
        if (id.Length == 0)
            throw new ArgumentException("Falta id de tienda.", nameof(body));

        var data = new StoreProfileWorkspaceData
        {
            Id = body.Id,
            Name = body.Name,
            Verified = body.Verified,
            Categories = body.Categories,
            TransportIncluded = body.TransportIncluded,
            TrustScore = body.TrustScore,
            AvatarUrl = body.AvatarUrl,
            Pitch = body.Pitch,
            OwnerUserId = body.OwnerUserId,
            Location = body.Location,
            WebsiteUrl = body.WebsiteUrl,
        };

        return new MarketWorkspacePatch
        {
            Stores = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal) { [id] = data },
        };
    }

    public static MarketWorkspacePatch ToStoreCatalogsPatch(WorkspaceStoreCatalogsPutRequest body)
    {
        var patch = new MarketWorkspacePatch();
        if (body.Stores is { Count: > 0 } s)
            patch.Stores = new Dictionary<string, StoreProfileWorkspaceData>(s, StringComparer.Ordinal);
        if (body.StoreCatalogs is { Count: > 0 } c)
            patch.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(c, StringComparer.Ordinal);
        return patch;
    }

    public static MarketWorkspacePatch ToOfferInquiriesPatch(WorkspaceInquiriesPutRequest body) =>
        new() { Offers = body.Offers is { Count: > 0 } o ? new Dictionary<string, HomeOfferViewDto>(o, StringComparer.Ordinal) : null };
}
