namespace VibeTrade.Backend.Features.Search;

public sealed record ElasticsearchStoreSearchHit(string StoreId, double? DistanceKm);

public sealed record ElasticsearchStoreSearchResult(
    IReadOnlyList<ElasticsearchStoreSearchHit> Hits,
    long TotalCount);

public interface IElasticsearchStoreSearchQuery
{
    bool IsConfigured { get; }

    Task<ElasticsearchStoreSearchResult?> SearchAsync(
        string? name,
        string? category,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
