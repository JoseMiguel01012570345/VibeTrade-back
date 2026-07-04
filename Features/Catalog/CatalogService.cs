using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Search.Catalog;
using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Catalog;

public sealed class CatalogService(
    AppDbContext db,
    ICatalogSearchLiveIndexSync catalogSearchLiveIndex,
    IOfferService offerService) : IMarketCatalogSyncService
{
    public async Task<Dictionary<string, StoreProfileWorkspaceData>> BuildStoresViewAsync(
        CancellationToken cancellationToken = default)
    {
        var o = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);
        var list = await db.Stores.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var s in list)
            o[s.Id] = MarketWorkspacePersistence.FromStoreRow(s);
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
                Products = products.Select(offerService.ProductCatalogRowFromEntity).ToList(),
                Services = services.Select(offerService.ServiceCatalogRowFromEntity).ToList(),
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
            Store = MarketWorkspacePersistence.FromStoreRow(store),
            Catalog = new StoreCatalogBlockView
            {
                Pitch = store.Pitch,
                JoinedAt = store.JoinedAtMs,
                Products = products.Select(offerService.ProductCatalogRowFromEntity).ToList(),
                Services = services.Select(offerService.ServiceCatalogRowFromEntity).ToList(),
            },
        };
    }

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

        await catalogSearchLiveIndex.SyncStoreAsync(sid, cancellationToken);

        return StoreCatalogUpsertResult.Ok;
    }

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
            entries.Add((p.UpdatedAt, p.Id, offerService.FromProductRow(p)));
        }

        foreach (var s in services)
        {
            if (!stores.ContainsKey(s.StoreId))
                continue;
            entries.Add((s.UpdatedAt, s.Id, offerService.FromServiceRow(s)));
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

        var map = await RecommendationBatchOfferLoader.BuildOffersViewInOrderAsync(db, offerService, new[] { oid }, cancellationToken);
        if (!map.TryGetValue(oid, out var offerView))
            return null;

        var storeId = (offerView.StoreId ?? "").Trim();
        if (string.IsNullOrEmpty(storeId))
            return new PublicOfferCardSnapshot(offerView, new StoreProfileWorkspaceData());

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
        var storeData = store is null
            ? MarketWorkspacePersistence.MinimalStub(storeId)
            : MarketWorkspacePersistence.FromStoreRow(store);
        return new PublicOfferCardSnapshot(offerView, storeData);
    }

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
        await catalogSearchLiveIndex.SyncStoreAsync(storeId, cancellationToken);
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
        await catalogSearchLiveIndex.SyncStoreAsync(storeId, cancellationToken);
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
        await catalogSearchLiveIndex.SyncStoreAsync(storeId, cancellationToken);
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
        await catalogSearchLiveIndex.SyncStoreAsync(storeId, cancellationToken);
        return StoreCatalogUpsertResult.Ok;
    }

    public async Task ApplyCoreAsync(
        MarketWorkspaceState workspaceRoot,
        bool storeProfiles,
        bool catalogs,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var hasStores = workspaceRoot.Stores.Count > 0;

        if (hasStores)
        {
            var catalogSync = catalogs;

            foreach (var kv in workspaceRoot.Stores)
            {
                var storeId = kv.Key;
                var el = kv.Value;
                var ownerUserId = (el.OwnerUserId ?? "").Trim();
                if (string.IsNullOrEmpty(ownerUserId))
                    ownerUserId = "unknown";

                await EnsureUserExistsAsync(ownerUserId, now, cancellationToken);

                var row = await db.Stores.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
                if (row is null)
                {
                    row = new StoreRow
                    {
                        Id = storeId,
                        CreatedAt = now,
                        JoinedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    };
                    db.Stores.Add(row);
                    MarketStoreRowWorkspaceMapper.ApplyFields(el, row, now);
                }
                else
                {
                    if (row.DeletedAtUtc is not null)
                        row.DeletedAtUtc = null;
                    if (storeProfiles)
                        MarketStoreRowWorkspaceMapper.ApplyFields(el, row, now);
                }

                if (catalogSync && workspaceRoot.StoreCatalogs.TryGetValue(storeId, out var catEl))
                {
                    row.Pitch = catEl.Pitch ?? "";
                    if (catEl.JoinedAt is { } joined)
                        row.JoinedAtMs = joined;

                    row.UpdatedAt = now;
                    await SyncProductsAsync(storeId, catEl, now, cancellationToken);
                    await SyncServicesAsync(storeId, catEl, now, cancellationToken);
                }
            }

            if (storeProfiles)
                MarketCatalogStoreDuplicateGuard.ThrowIfDuplicateNormalizedNames(db);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException(DuplicateStoreNameConflict.Message);
        }

        if (hasStores && (storeProfiles || catalogs))
        {
            var ids = workspaceRoot.Stores.Select(p => p.Key).ToList();
            await catalogSearchLiveIndex.SyncStoresAsync(ids, cancellationToken);
        }
    }

    private async Task SyncProductsAsync(
        string storeId,
        StoreCatalogBlockView catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var arr = catalogEl.Products ?? Array.Empty<StoreProductCatalogRowView>();

        var incomingIds = new HashSet<string>();
        foreach (var o in arr)
        {
            if (!string.IsNullOrEmpty(o.Id))
                incomingIds.Add(o.Id);
        }

        var stale = await db.StoreProducts
            .Where(p => p.StoreId == storeId && !incomingIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        foreach (var p in stale)
            p.DeletedAtUtc = now;

        foreach (var item in arr)
        {
            var id = item.Id;
            if (string.IsNullOrEmpty(id))
                continue;
            var p = item.ToPutRequest();
            await UpsertSingleProductRowAsync(storeId, id, p, now, cancellationToken);
        }
    }

    private async Task SyncServicesAsync(
        string storeId,
        StoreCatalogBlockView catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var arr = catalogEl.Services ?? Array.Empty<StoreServiceCatalogRowView>();

        var incomingIds = new HashSet<string>();
        foreach (var o in arr)
        {
            if (!string.IsNullOrEmpty(o.Id))
                incomingIds.Add(o.Id);
        }

        var stale = await db.StoreServices
            .Where(s => s.StoreId == storeId && !incomingIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        foreach (var s in stale)
            s.DeletedAtUtc = now;

        foreach (var item in arr)
        {
            var id = item.Id;
            if (string.IsNullOrEmpty(id))
                continue;
            var s = item.ToPutRequest();
            await UpsertSingleServiceRowAsync(storeId, id, s, now, cancellationToken);
        }
    }

    private async Task EnsureUserExistsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.UserAccounts.Local.Any(u => u.Id == userId))
            return;
        if (await db.UserAccounts.AnyAsync(u => u.Id == userId, cancellationToken))
            return;
        db.UserAccounts.Add(new UserAccount
        {
            Id = userId,
            DisplayName = "Usuario",
            TrustScore = 75,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private async Task UpsertSingleProductRowAsync(
        string storeId,
        string id,
        StoreProductPutRequest p,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        MarketCatalogCurrency.ThrowIfProductCurrencyInvalid(p, id);

        var row = await db.StoreProducts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is not null && row.StoreId != storeId)
            return;

        if (row is null)
        {
            row = new StoreProductRow { Id = id, StoreId = storeId };
            db.StoreProducts.Add(row);
        }
        else
            row.DeletedAtUtc = null;

        MarketCatalogProductRowMapper.Apply(p, row, now);
    }

    private async Task UpsertSingleServiceRowAsync(
        string storeId,
        string id,
        StoreServicePutRequest s,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        MarketCatalogCurrency.ThrowIfServiceCurrencyInvalid(s, id);

        var row = await db.StoreServices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is not null && row.StoreId != storeId)
            return;

        if (row is null)
        {
            row = new StoreServiceRow { Id = id, StoreId = storeId };
            db.StoreServices.Add(row);
        }
        else
            row.DeletedAtUtc = null;

        MarketCatalogServiceRowMapper.Apply(s, row, now);
        if (s.PhotoUrls is not null)
        {
            row.PhotoUrls = await MarketCatalogIncomingServicePhotos.FilterToStoredImageListAsync(
                db,
                s.PhotoUrls,
                cancellationToken);
        }

        if (MarketCatalogTransportServiceRules.QualifiesAsTransport(row.Category, row.NombreServicio)
            && !MarketCatalogTransportServiceRules.HasAtLeastOnePhoto(row.PhotoUrls))
            throw new ArgumentException(
                "Los servicios de transporte o logística requieren al menos una imagen en la ficha.",
                CatalogArgumentParams.Validation);
    }

    private async Task RemoveStoreOffersFromPersistedWorkspaceAsync(
        HashSet<string> offerKeys,
        CancellationToken cancellationToken)
    {
        if (offerKeys.Count == 0)
            return;

        var fromDb = await MarketWorkspacePersistence.GetPersistedWorkspaceAsync(db, cancellationToken);
        if (fromDb is null)
            return;

        var merged = CloneWorkspaceState(fromDb);
        foreach (var oid in offerKeys)
            merged.Offers.Remove(oid);

        merged.OfferIds.RemoveAll(id => offerKeys.Contains(id));

        var slim = CloneWorkspaceState(merged);
        slim.Stores = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);
        slim.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(StringComparer.Ordinal);
        MarketWorkspacePersistence.ValidateWorkspaceForPersist(slim);
        await MarketWorkspacePersistence.SavePersistedWorkspaceAsync(db, slim, cancellationToken);
    }

    private static MarketWorkspaceState CloneWorkspaceState(MarketWorkspaceState s) =>
        JsonSerializer.Deserialize<MarketWorkspaceState>(
            JsonSerializer.Serialize(s, MarketJsonDefaults.Options), MarketJsonDefaults.Options)
        ?? new MarketWorkspaceState();
}
