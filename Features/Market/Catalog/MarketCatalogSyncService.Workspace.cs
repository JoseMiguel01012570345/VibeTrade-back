using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Search;
using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Market.Catalog;

public sealed partial class MarketCatalogSyncService
{
    private async Task ApplyCoreAsync(
        MarketWorkspaceState workspaceRoot,
        bool storeProfiles,
        bool catalogs,
        bool offerQa,
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

        if (offerQa)
            ApplyOfferQaFromWorkspace(workspaceRoot, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateStoreNameException(null);
        }

        if (offerQa)
        {
            foreach (var prop in workspaceRoot.Offers)
                await chat.SyncOfferQaAnswersForOfferAsync(prop.Key, cancellationToken);
        }

        if (hasStores && (storeProfiles || catalogs))
        {
            var ids = workspaceRoot.Stores.Select(p => p.Key).ToList();
            await storeSearchIndex.UpsertStoresAsync(ids, cancellationToken);
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

        if (MarketCatalogTransportServiceRules.QualifiesAsTransport(row.Category, row.TipoServicio)
            && !MarketCatalogTransportServiceRules.HasAtLeastOnePhoto(row.PhotoUrls))
            throw new CatalogValidationException(
                "Los servicios de transporte o logística requieren al menos una imagen en la ficha.");
    }

    private void ApplyOfferQaFromWorkspace(MarketWorkspaceState workspaceRoot, DateTimeOffset now)
    {
        if (workspaceRoot.Offers.Count == 0)
            return;

        foreach (var kv in workspaceRoot.Offers)
        {
            if (kv.Value.Qa is null)
                continue;

            var qaList = kv.Value.Qa;
            var id = kv.Key;

            var product = db.StoreProducts.Find(id);
            if (product is not null)
            {
                product.OfferQa = qaList;
                product.UpdatedAt = now;
                continue;
            }

            var service = db.StoreServices.Find(id);
            if (service is not null)
            {
                service.OfferQa = qaList;
                service.UpdatedAt = now;
            }
        }
    }
}
