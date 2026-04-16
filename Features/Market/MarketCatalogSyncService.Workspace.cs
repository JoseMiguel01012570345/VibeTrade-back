using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService
{
    private async Task ApplyCoreAsync(
        JsonElement workspaceRoot,
        bool storeProfiles,
        bool catalogs,
        bool offerQa,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var hasStores = MarketCatalogJsonHelpers.TryGetStoresObject(workspaceRoot, out var storesEl);

        if (hasStores)
        {
            var hasCatalogsObj = workspaceRoot.TryGetProperty("storeCatalogs", out var catalogsEl)
                                 && catalogsEl.ValueKind == JsonValueKind.Object;
            var catalogSync = catalogs && hasCatalogsObj;

            foreach (var prop in storesEl.EnumerateObject())
            {
                var storeId = prop.Name;
                var el = prop.Value;
                var ownerUserId = el.TryGetProperty("ownerUserId", out var ou) && ou.ValueKind == JsonValueKind.String
                    ? ou.GetString()!
                    : "unknown";

                await EnsureUserExistsAsync(ownerUserId, now, cancellationToken);

                var row = await db.Stores.FindAsync([storeId], cancellationToken);
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
                else if (storeProfiles)
                {
                    MarketStoreRowWorkspaceMapper.ApplyFields(el, row, now);
                }

                if (catalogSync && catalogsEl.TryGetProperty(storeId, out var catEl) && catEl.ValueKind == JsonValueKind.Object)
                {
                    row.Pitch = MarketCatalogJsonHelpers.GetString(catEl, "pitch") ?? "";
                    row.JoinedAtMs = catEl.TryGetProperty("joinedAt", out var ja) && ja.TryGetInt64(out var jn) ? jn : row.JoinedAtMs;
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

        if (offerQa
            && workspaceRoot.TryGetProperty("offers", out var offersForChatEl)
            && offersForChatEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in offersForChatEl.EnumerateObject())
                await chat.SyncOfferQaAnswersForOfferAsync(prop.Name, cancellationToken);
        }

        if (hasStores && (storeProfiles || catalogs))
        {
            var ids = storesEl.EnumerateObject().Select(p => p.Name).ToList();
            await storeSearchIndex.UpsertStoresAsync(ids, cancellationToken);
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

    private async Task SyncProductsAsync(
        string storeId,
        JsonElement catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!catalogEl.TryGetProperty("products", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var incomingIds = new HashSet<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                incomingIds.Add(idEl.GetString()!);
        }

        var stale = await db.StoreProducts
            .Where(p => p.StoreId == storeId && !incomingIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        db.StoreProducts.RemoveRange(stale);

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = MarketCatalogJsonHelpers.GetString(item, "id");
            if (string.IsNullOrEmpty(id))
                continue;
            await UpsertSingleProductRowAsync(storeId, id, item, now, cancellationToken);
        }
    }

    private async Task SyncServicesAsync(
        string storeId,
        JsonElement catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!catalogEl.TryGetProperty("services", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var incomingIds = new HashSet<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                incomingIds.Add(idEl.GetString()!);
        }

        var stale = await db.StoreServices
            .Where(s => s.StoreId == storeId && !incomingIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        db.StoreServices.RemoveRange(stale);

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = MarketCatalogJsonHelpers.GetString(item, "id");
            if (string.IsNullOrEmpty(id))
                continue;
            await UpsertSingleServiceRowAsync(storeId, id, item, now, cancellationToken);
        }
    }

    private async Task UpsertSingleProductRowAsync(
        string storeId,
        string id,
        JsonElement item,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        MarketCatalogCurrency.ThrowIfProductCurrencyInvalid(item, id);

        var row = await db.StoreProducts.FindAsync([id], cancellationToken);
        if (row is not null && row.StoreId != storeId)
            return;

        if (row is null)
        {
            row = new StoreProductRow { Id = id, StoreId = storeId };
            db.StoreProducts.Add(row);
        }

        MarketCatalogProductRowMapper.Apply(item, row, now);
    }

    private async Task UpsertSingleServiceRowAsync(
        string storeId,
        string id,
        JsonElement item,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        MarketCatalogCurrency.ThrowIfServiceCurrencyInvalid(item, id);

        var row = await db.StoreServices.FindAsync([id], cancellationToken);
        if (row is not null && row.StoreId != storeId)
            return;

        if (row is null)
        {
            row = new StoreServiceRow { Id = id, StoreId = storeId };
            db.StoreServices.Add(row);
        }

        MarketCatalogServiceRowMapper.Apply(item, row, now);
        if (item.TryGetProperty("photoUrls", out var phEl) && phEl.ValueKind == JsonValueKind.Array)
            row.PhotoUrlsJson = await MarketCatalogIncomingServicePhotos.FilterToStoredImageJsonAsync(db, phEl, cancellationToken);
    }

    private void ApplyOfferQaFromWorkspace(JsonElement workspaceRoot, DateTimeOffset now)
    {
        if (!workspaceRoot.TryGetProperty("offers", out var offersEl) || offersEl.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in offersEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!prop.Value.TryGetProperty("qa", out var qaEl) || qaEl.ValueKind != JsonValueKind.Array)
                continue;

            var qaRaw = qaEl.GetRawText();
            var id = prop.Name;

            var product = db.StoreProducts.Find(id);
            if (product is not null)
            {
                product.OfferQaJson = qaRaw;
                product.UpdatedAt = now;
                continue;
            }

            var service = db.StoreServices.Find(id);
            if (service is not null)
            {
                service.OfferQaJson = qaRaw;
                service.UpdatedAt = now;
            }
        }
    }
}
