namespace VibeTrade.Backend.Features.Market.Interfaces;

/// <summary>Sincroniza tiendas, productos y servicios entre PostgreSQL y el shape JSON del cliente.</summary>
public interface IMarketCatalogSyncService
{
    Task ApplyStoreProfilesFromWorkspaceAsync(
        MarketWorkspaceState workspaceRoot,
        CancellationToken cancellationToken = default);

    Task ApplyStoreCatalogsFromWorkspaceAsync(
        MarketWorkspaceState workspaceRoot,
        CancellationToken cancellationToken = default);

    Task ApplyOfferInquiriesFromWorkspaceAsync(
        MarketWorkspaceState workspaceRoot,
        CancellationToken cancellationToken = default);

    Task<OfferQaComment?> AppendOfferInquiryAsync(
        string offerId,
        string text,
        string? parentId,
        string askedById,
        string askedByName,
        int trustScore,
        long? createdAtMs,
        CancellationToken cancellationToken = default);

    Task<string?> TryGetOfferCommentAuthorIdAsync(string offerId, string commentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfferQaComment>?> GetOfferQaForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<PublicOfferCardSnapshot?> TryGetPublicOfferCardAsync(string offerId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, StoreProfileWorkspaceData>> BuildStoresViewAsync(CancellationToken cancellationToken = default);

    Task<Dictionary<string, StoreCatalogBlockView>> BuildStoreCatalogsViewAsync(CancellationToken cancellationToken = default);

    Task<StoreWithCatalogDetailView?> GetStoreDetailViewAsync(string storeId, CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> UpsertStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        StoreProductPutRequest product,
        CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> DeleteStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> UpsertStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        StoreServicePutRequest service,
        CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> DeleteStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>Elimina la tienda y su catálogo relacional; solo el dueño (<paramref name="userId"/>).</summary>
    Task<StoreCatalogUpsertResult> DeleteStoreAsync(
        string storeId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<(Dictionary<string, HomeOfferViewDto> Offers, List<string> OfferIds)> BuildPublishedOffersFeedAsync(
        CancellationToken cancellationToken = default);
}
