using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Catalog;

public sealed partial class MarketCatalogSyncService
{
    public async Task<StoreCatalogUpsertResult> UpsertStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        StoreProductPutRequest product,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        if (product.Id != productId)
            return StoreCatalogUpsertResult.IdMismatch;

        var existing = await db.StoreProducts.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (existing is not null && existing.StoreId != storeId)
            return StoreCatalogUpsertResult.Forbidden;

        var now = DateTimeOffset.UtcNow;
        await UpsertSingleProductRowAsync(storeId, productId, product, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }

    public async Task<StoreCatalogUpsertResult> DeleteStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        var row = await db.StoreProducts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken);
        if (row is null)
            return StoreCatalogUpsertResult.EntityNotFound;

        if (row.DeletedAtUtc is not null)
            return StoreCatalogUpsertResult.Ok;

        row.DeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }

    public async Task<StoreCatalogUpsertResult> UpsertStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        StoreServicePutRequest service,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        if (service.Id != serviceId)
            return StoreCatalogUpsertResult.IdMismatch;

        var existing = await db.StoreServices.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);
        if (existing is not null && existing.StoreId != storeId)
            return StoreCatalogUpsertResult.Forbidden;

        var now = DateTimeOffset.UtcNow;
        await UpsertSingleServiceRowAsync(storeId, serviceId, service, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }

    public async Task<StoreCatalogUpsertResult> DeleteStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        var row = await db.StoreServices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.StoreId == storeId, cancellationToken);
        if (row is null)
            return StoreCatalogUpsertResult.EntityNotFound;

        if (row.DeletedAtUtc is not null)
            return StoreCatalogUpsertResult.Ok;

        row.DeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }
}
