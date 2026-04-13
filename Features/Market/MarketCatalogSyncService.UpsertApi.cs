using System.Text.Json;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService
{
    public async Task<StoreCatalogUpsertResult> UpsertStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        JsonElement product,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        if (product.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            if (idEl.GetString() != productId)
                return StoreCatalogUpsertResult.IdMismatch;
        }

        var existing = await db.StoreProducts.FindAsync([productId], cancellationToken);
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

        var row = await db.StoreProducts.FindAsync([productId], cancellationToken);
        if (row is null || row.StoreId != storeId)
            return StoreCatalogUpsertResult.EntityNotFound;

        db.StoreProducts.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }

    public async Task<StoreCatalogUpsertResult> UpsertStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        JsonElement service,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var store = await db.Stores.FindAsync([storeId], cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        if (service.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            if (idEl.GetString() != serviceId)
                return StoreCatalogUpsertResult.IdMismatch;
        }

        var existing = await db.StoreServices.FindAsync([serviceId], cancellationToken);
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

        var row = await db.StoreServices.FindAsync([serviceId], cancellationToken);
        if (row is null || row.StoreId != storeId)
            return StoreCatalogUpsertResult.EntityNotFound;

        db.StoreServices.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await storeSearchIndex.UpsertStoresAsync([storeId], cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }
}
