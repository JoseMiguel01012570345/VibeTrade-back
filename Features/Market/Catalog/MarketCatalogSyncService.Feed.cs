using Microsoft.EntityFrameworkCore;

namespace VibeTrade.Backend.Features.Market.Catalog;

public sealed partial class MarketCatalogSyncService
{
    public async Task<(Dictionary<string, HomeOfferViewDto> Offers, List<string> OfferIds)> BuildPublishedOffersFeedAsync(
        CancellationToken cancellationToken = default)
    {
        var stores = await db.Stores.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, cancellationToken);

        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published)
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => s.Published == null || s.Published == true)
            .ToListAsync(cancellationToken);

        var entries = new List<(DateTimeOffset at, string id, HomeOfferViewDto offer)>(
            capacity: products.Count + services.Count);

        foreach (var p in products)
        {
            if (!stores.ContainsKey(p.StoreId))
                continue;
            entries.Add((p.UpdatedAt, p.Id, HomeOfferViewFactory.FromProductRow(p)));
        }

        foreach (var s in services)
        {
            if (!stores.ContainsKey(s.StoreId))
                continue;
            entries.Add((s.UpdatedAt, s.Id, HomeOfferViewFactory.FromServiceRow(s)));
        }

        entries.Sort((a, b) => b.at.CompareTo(a.at));

        var offersObj = new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var (_, id, offer) in entries)
        {
            offersObj[id] = offer;
            ids.Add(id);
        }

        return (offersObj, ids);
    }
}
