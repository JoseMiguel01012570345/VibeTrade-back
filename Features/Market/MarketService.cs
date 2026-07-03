using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Features.Market;

/// <summary>
/// Workspace de mercado: persistencia en <c>market_workspaces</c>, validación del JSON persistido
/// y orquestación con catálogo. La persistencia expuesta como estáticos internos evita ciclos de DI con <see cref="IMarketCatalogSyncService"/>.
/// </summary>
public sealed class MarketService(AppDbContext db, IMarketCatalogSyncService catalog) : IMarketWorkspaceService
{
    public async Task<MarketWorkspaceState> GetOrSeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await MarketWorkspacePersistence.GetPersistedWorkspaceAsync(db, cancellationToken);
        if (existing is null)
        {
            var seed = new MarketWorkspaceState();
            MarketWorkspacePersistence.ValidateWorkspaceForPersist(seed);
            await MarketWorkspacePersistence.SavePersistedWorkspaceAsync(db, seed, cancellationToken);
            existing = await MarketWorkspacePersistence.GetPersistedWorkspaceAsync(db, cancellationToken);
        }

        if (existing is null)
            throw new InvalidOperationException("Market workspace row is missing after seed.");

        var root = CloneState(existing);
        root.Stores = await catalog.BuildStoresViewAsync(cancellationToken);
        root.StoreCatalogs = await catalog.BuildStoreCatalogsViewAsync(cancellationToken);
        (root.Offers, root.OfferIds) = await catalog.BuildPublishedOffersFeedAsync(cancellationToken);

        return root;
    }

    public Task<StoreWithCatalogDetailView?> GetStoreDetailAsync(
        string storeId,
        CancellationToken cancellationToken = default) =>
        catalog.GetStoreDetailViewAsync(storeId, cancellationToken);

    public async Task<MarketWorkspaceState> GetStoresSnapshotAsync(CancellationToken cancellationToken = default) =>
        new() { Stores = await catalog.BuildStoresViewAsync(cancellationToken) };

    public Task SaveStoreProfilesAsync(WorkspaceStorePutRequest body, CancellationToken cancellationToken = default) =>
        SaveFromPatchAsync(
            ToStoreProfilesPatch(body),
            static (c, el, ct) => c.ApplyCoreAsync(el, storeProfiles: true, catalogs: false, offerQa: false, ct),
            cancellationToken);

    public Task SaveStoreCatalogsAsync(WorkspaceStoreCatalogsPutRequest body, CancellationToken cancellationToken = default) =>
        SaveFromPatchAsync(
            ToStoreCatalogsPatch(body),
            static (c, el, ct) => c.ApplyCoreAsync(el, storeProfiles: false, catalogs: true, offerQa: false, ct),
            cancellationToken);

    public Task SaveOfferInquiriesAsync(WorkspaceInquiriesPutRequest body, CancellationToken cancellationToken = default) =>
        SaveFromPatchAsync(
            ToOfferInquiriesPatch(body),
            static (c, el, ct) => c.ApplyCoreAsync(el, storeProfiles: false, catalogs: false, offerQa: true, ct),
            cancellationToken);

    private async Task SaveFromPatchAsync(
        MarketWorkspacePatch patch,
        Func<IMarketCatalogSyncService, MarketWorkspaceState, CancellationToken, Task> applyRelational,
        CancellationToken cancellationToken)
    {
        var fromDb = await MarketWorkspacePersistence.GetPersistedWorkspaceAsync(db, cancellationToken) ?? new MarketWorkspaceState();
        var merged = MergeWorkspacePatch(CloneState(fromDb), patch);
        await applyRelational(catalog, merged, cancellationToken);
        var slim = CloneState(merged);
        slim.Stores = new(StringComparer.Ordinal);
        slim.StoreCatalogs = new(StringComparer.Ordinal);
        MarketWorkspacePersistence.ValidateWorkspaceForPersist(slim);
        await MarketWorkspacePersistence.SavePersistedWorkspaceAsync(db, slim, cancellationToken);
    }

    private static MarketWorkspaceState CloneState(MarketWorkspaceState s) =>
        JsonSerializer.Deserialize<MarketWorkspaceState>(
            JsonSerializer.Serialize(s, MarketJsonDefaults.Options), MarketJsonDefaults.Options)
        ?? new MarketWorkspaceState();

    private static MarketWorkspaceState MergeWorkspacePatch(MarketWorkspaceState existing, MarketWorkspacePatch patch)
    {
        if (patch.Stores is not null)
        {
            foreach (var kv in patch.Stores)
                existing.Stores[kv.Key] = kv.Value;
        }

        if (patch.Offers is not null)
        {
            foreach (var kv in patch.Offers)
                existing.Offers[kv.Key] = kv.Value;
        }

        if (patch.OfferIds is not null)
            existing.OfferIds = new List<string>(patch.OfferIds);

        if (patch.StoreCatalogs is not null)
        {
            foreach (var kv in patch.StoreCatalogs)
                existing.StoreCatalogs[kv.Key] = kv.Value;
        }

        if (patch.Threads is not null)
        {
            foreach (var kv in patch.Threads)
                existing.Threads[kv.Key] = kv.Value;
        }

        if (patch.RouteOfferPublic is not null)
        {
            foreach (var kv in patch.RouteOfferPublic)
                existing.RouteOfferPublic[kv.Key] = kv.Value;
        }

        return existing;
    }

    private static MarketWorkspacePatch ToStoreProfilesPatch(WorkspaceStorePutRequest body)
    {
        if (body.Stores is { Count: > 0 } byId)
            return new MarketWorkspacePatch { Stores = new Dictionary<string, StoreProfileWorkspaceData>(byId, StringComparer.Ordinal) };

        var id = (body.Id ?? "").Trim();
        if (id.Length == 0)
            throw new ArgumentException("Falta id de tienda.", nameof(body));

        var data = new StoreProfileWorkspaceData
        {
            Id = body.Id,
            Name = body.Name,
            Verified = body.Verified,
            Categories = body.Categories,
            TransportIncluded = body.TransportIncluded,
            TrustScore = body.TrustScore,
            AvatarUrl = body.AvatarUrl,
            Pitch = body.Pitch,
            OwnerUserId = body.OwnerUserId,
            Location = body.Location,
            WebsiteUrl = body.WebsiteUrl,
            PricePerKm = body.PricePerKm,
            PricePerKmCurrencyCode = body.PricePerKmCurrencyCode,
        };

        return new MarketWorkspacePatch
        {
            Stores = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal) { [id] = data },
        };
    }

    private static MarketWorkspacePatch ToStoreCatalogsPatch(WorkspaceStoreCatalogsPutRequest body)
    {
        var patch = new MarketWorkspacePatch();
        if (body.Stores is { Count: > 0 } s)
            patch.Stores = new Dictionary<string, StoreProfileWorkspaceData>(s, StringComparer.Ordinal);
        if (body.StoreCatalogs is { Count: > 0 } c)
            patch.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(c, StringComparer.Ordinal);
        return patch;
    }

    private static MarketWorkspacePatch ToOfferInquiriesPatch(WorkspaceInquiriesPutRequest body) =>
        new() { Offers = body.Offers is { Count: > 0 } o ? new Dictionary<string, HomeOfferViewDto>(o, StringComparer.Ordinal) : null };
}
