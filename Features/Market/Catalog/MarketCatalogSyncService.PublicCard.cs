using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations.Core;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Popularity;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Market.Catalog;

public sealed partial class MarketCatalogSyncService
{
    public async Task<PublicOfferCardSnapshot?> TryGetPublicOfferCardAsync(
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return null;

        // Placeholder de hilos solo mensajería (misma constante que ChatService.SocialThreadOfferId).
        if (string.Equals(oid, "__vt_social__", StringComparison.Ordinal))
        {
            var synthetic = new HomeOfferViewDto
            {
                Id = oid,
                StoreId = "",
                Title = "Chat",
                Price = "\u2014",
                Tags = new List<string>(),
                ImageUrl = "",
                ImageUrls = Array.Empty<string>(),
            };
            return new PublicOfferCardSnapshot(synthetic, new StoreProfileWorkspaceData());
        }

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
