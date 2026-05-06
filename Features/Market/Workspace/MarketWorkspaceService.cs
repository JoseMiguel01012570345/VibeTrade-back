using System.Text.Json;

namespace VibeTrade.Backend.Features.Market.Workspace;

public sealed class MarketWorkspaceService(
    IMarketWorkspaceRepository repository,
    IMarketWorkspaceIntegrity integrity,
    IMarketCatalogSyncService catalog) : IMarketWorkspaceService
{
    public async Task<MarketWorkspaceState> GetOrSeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is null)
        {
            var seed = new MarketWorkspaceState();
            integrity.ValidateOrThrow(seed);
            await repository.SaveAsync(seed, cancellationToken);
            existing = await repository.GetAsync(cancellationToken);
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
            MarketWorkspaceRequestMapper.ToStoreProfilesPatch(body),
            static (c, el, ct) => c.ApplyStoreProfilesFromWorkspaceAsync(el, ct),
            cancellationToken);

    public Task SaveStoreCatalogsAsync(WorkspaceStoreCatalogsPutRequest body, CancellationToken cancellationToken = default) =>
        SaveFromPatchAsync(
            MarketWorkspaceRequestMapper.ToStoreCatalogsPatch(body),
            static (c, el, ct) => c.ApplyStoreCatalogsFromWorkspaceAsync(el, ct),
            cancellationToken);

    public Task SaveOfferInquiriesAsync(WorkspaceInquiriesPutRequest body, CancellationToken cancellationToken = default) =>
        SaveFromPatchAsync(
            MarketWorkspaceRequestMapper.ToOfferInquiriesPatch(body),
            static (c, el, ct) => c.ApplyOfferInquiriesFromWorkspaceAsync(el, ct),
            cancellationToken);

    private async Task SaveFromPatchAsync(
        MarketWorkspacePatch patch,
        Func<IMarketCatalogSyncService, MarketWorkspaceState, CancellationToken, Task> applyRelational,
        CancellationToken cancellationToken)
    {
        var fromDb = await repository.GetAsync(cancellationToken) ?? new MarketWorkspaceState();
        var merged = MarketWorkspacePatch.Merge(CloneState(fromDb), patch);
        await applyRelational(catalog, merged, cancellationToken);
        var slim = CloneState(merged);
        slim.Stores = new(StringComparer.Ordinal);
        slim.StoreCatalogs = new(StringComparer.Ordinal);
        integrity.ValidateOrThrow(slim);
        await repository.SaveAsync(slim, cancellationToken);
    }

    private static MarketWorkspaceState CloneState(MarketWorkspaceState s) =>
        JsonSerializer.Deserialize<MarketWorkspaceState>(
            JsonSerializer.Serialize(s, MarketJsonDefaults.Options), MarketJsonDefaults.Options)
        ?? new MarketWorkspaceState();
}
