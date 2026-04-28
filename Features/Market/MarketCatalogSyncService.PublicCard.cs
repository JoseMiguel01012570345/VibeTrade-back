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

        var map = await RecommendationBatchOfferLoader.BuildOffersViewInOrderAsync(db, new[] { oid }, cancellationToken);
        if (!map.TryGetValue(oid, out var offerView))
            return null;

        var storeId = (offerView.StoreId ?? "").Trim();
        if (string.IsNullOrEmpty(storeId))
            return new PublicOfferCardSnapshot(offerView, new StoreProfileWorkspaceData());

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        var storeData = store is null
            ? StoreProfileWorkspaceData.MinimalStub(storeId)
            : StoreProfileWorkspaceData.FromStoreRow(store);
        return new PublicOfferCardSnapshot(offerView, storeData);
    }
}
