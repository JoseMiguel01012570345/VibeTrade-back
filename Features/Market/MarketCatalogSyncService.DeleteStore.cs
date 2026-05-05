using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService
{
    public async Task<StoreCatalogUpsertResult> DeleteStoreAsync(
        string storeId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StoreCatalogUpsertResult.Unauthorized;

        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return StoreCatalogUpsertResult.StoreNotFound;

        var store = await db.Stores
            .FirstOrDefaultAsync(s => s.Id == sid, cancellationToken);
        if (store is null)
            return StoreCatalogUpsertResult.StoreNotFound;
        if (store.OwnerUserId != userId)
            return StoreCatalogUpsertResult.Forbidden;

        var products = await db.StoreProducts.IgnoreQueryFilters()
            .Where(p => p.StoreId == sid)
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.IgnoreQueryFilters()
            .Where(s => s.StoreId == sid)
            .ToListAsync(cancellationToken);
        var offerKeys = products.Select(p => p.Id).Concat(services.Select(s => s.Id))
            .ToHashSet(StringComparer.Ordinal);

        await RemoveStoreOffersFromPersistedWorkspaceAsync(offerKeys, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var p in products)
            p.DeletedAtUtc = now;
        foreach (var s in services)
            s.DeletedAtUtc = now;

        store.DeletedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        await storeSearchIndex.UpsertStoresAsync([sid], cancellationToken);

        return StoreCatalogUpsertResult.Ok;
    }

    private async Task RemoveStoreOffersFromPersistedWorkspaceAsync(
        HashSet<string> offerKeys,
        CancellationToken cancellationToken)
    {
        if (offerKeys.Count == 0)
            return;

        var fromDb = await workspaceRepository.GetAsync(cancellationToken);
        if (fromDb is null)
            return;

        var merged = CloneWorkspaceState(fromDb);
        foreach (var oid in offerKeys)
            merged.Offers.Remove(oid);

        merged.OfferIds.RemoveAll(id => offerKeys.Contains(id));

        var slim = CloneWorkspaceState(merged);
        slim.Stores = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);
        slim.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(StringComparer.Ordinal);
        workspaceIntegrity.ValidateOrThrow(slim);
        await workspaceRepository.SaveAsync(slim, cancellationToken);
    }

    private static MarketWorkspaceState CloneWorkspaceState(MarketWorkspaceState s) =>
        JsonSerializer.Deserialize<MarketWorkspaceState>(
            JsonSerializer.Serialize(s, MarketJsonDefaults.Options), MarketJsonDefaults.Options)
        ?? new MarketWorkspaceState();
}
