using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService
{
    public async Task<PublicOfferCardSnapshot?> TryGetPublicOfferCardAsync(
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return null;

        var map = await RecommendationBatchOfferLoader.BuildOffersJsonInOrderAsync(db, new[] { oid }, cancellationToken);
        if (!map.TryGetPropertyValue(oid, out var offerNode) || offerNode is not JsonObject offerJson)
            return null;

        var storeId = offerJson["storeId"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(storeId))
            return new PublicOfferCardSnapshot(offerJson, new JsonObject());

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        var storeNode = store is null
            ? MarketCatalogStoreBadgeJson.MinimalStub(storeId)
            : MarketCatalogStoreBadgeJson.FromStoreRow(store);
        return new PublicOfferCardSnapshot(offerJson, storeNode);
    }
}
