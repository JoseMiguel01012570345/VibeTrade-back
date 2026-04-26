namespace VibeTrade.Backend.Features.Market;

public interface IMarketWorkspaceService
{
    Task<MarketWorkspaceState> GetOrSeedAsync(CancellationToken cancellationToken = default);

    Task<MarketWorkspaceState> GetStoresSnapshotAsync(CancellationToken cancellationToken = default);

    Task SaveStoreProfilesAsync(WorkspaceStorePutRequest body, CancellationToken cancellationToken = default);

    Task SaveStoreCatalogsAsync(WorkspaceStoreCatalogsPutRequest body, CancellationToken cancellationToken = default);

    Task SaveOfferInquiriesAsync(WorkspaceInquiriesPutRequest body, CancellationToken cancellationToken = default);

    Task<StoreWithCatalogDetailView?> GetStoreDetailAsync(string storeId, CancellationToken cancellationToken = default);
}
