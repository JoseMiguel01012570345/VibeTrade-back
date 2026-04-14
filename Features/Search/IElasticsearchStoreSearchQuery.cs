namespace VibeTrade.Backend.Features.Search;

public sealed record ElasticsearchStoreSearchHit(
    string DocumentId,
    string Kind,
    string StoreId,
    string? OfferId,
    double? DistanceKm);

public sealed record ElasticsearchStoreSearchResult(
    IReadOnlyList<ElasticsearchStoreSearchHit> Hits,
    long TotalCount);

public interface IElasticsearchStoreSearchQuery
{
    bool IsConfigured { get; }

    Task<ElasticsearchStoreSearchResult?> SearchAsync(
        string? name,
        string? category,
        IReadOnlyList<string> kinds,
        int? trustMin,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
