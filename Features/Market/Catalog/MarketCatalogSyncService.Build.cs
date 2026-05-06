using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market.Catalog;

public sealed partial class MarketCatalogSyncService
{
    public async Task<Dictionary<string, StoreProfileWorkspaceData>> BuildStoresViewAsync(
        CancellationToken cancellationToken = default)
    {
        var o = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);
        var list = await db.Stores.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var s in list)
            o[s.Id] = StoreProfileWorkspaceData.FromStoreRow(s);
        return o;
    }

    public async Task<Dictionary<string, StoreCatalogBlockView>> BuildStoreCatalogsViewAsync(
        CancellationToken cancellationToken = default)
    {
        var root = new Dictionary<string, StoreCatalogBlockView>(StringComparer.Ordinal);
        var storeIds = await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken);
        foreach (var storeId in storeIds)
        {
            var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == storeId, cancellationToken);
            var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

            root[storeId] = new StoreCatalogBlockView
            {
                Pitch = store.Pitch,
                JoinedAt = store.JoinedAtMs,
                Products = products.Select(MarketCatalogRowViewFactory.ProductFromRow).ToList(),
                Services = services.Select(MarketCatalogRowViewFactory.ServiceFromRow).ToList(),
            };
        }

        return root;
    }

    public async Task<StoreWithCatalogDetailView?> GetStoreDetailViewAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (store is null)
            return null;

        var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

        return new StoreWithCatalogDetailView
        {
            Store = StoreProfileWorkspaceData.FromStoreRow(store),
            Catalog = new StoreCatalogBlockView
            {
                Pitch = store.Pitch,
                JoinedAt = store.JoinedAtMs,
                Products = products.Select(MarketCatalogRowViewFactory.ProductFromRow).ToList(),
                Services = services.Select(MarketCatalogRowViewFactory.ServiceFromRow).ToList(),
            },
        };
    }
}
