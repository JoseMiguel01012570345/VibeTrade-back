namespace VibeTrade.Backend.Features.Market;

public interface IMarketCatalogStoreSearchService
{
    Task<StoreSearchResponse> SearchCatalogAsync(
        string? name,
        string? category,
        string? kinds,
        int? trustMin,
        double? lat,
        double? lng,
        double? km,
        int? limit,
        int? offset,
        CancellationToken cancellationToken);

    Task<StoreAutocompleteResponse> AutocompleteCatalogAsync(
        string? q,
        string? category,
        string? kinds,
        int? limit,
        CancellationToken cancellationToken);
}
