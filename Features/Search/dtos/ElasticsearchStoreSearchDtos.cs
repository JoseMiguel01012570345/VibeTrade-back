namespace VibeTrade.Backend.Features.Search.Dtos;

public sealed record ElasticsearchStoreSearchHit(
    string DocumentId,
    string Kind,
    string StoreId,
    string? OfferId,
    double? DistanceKm);

public sealed record ElasticsearchStoreSearchResult(
    IReadOnlyList<ElasticsearchStoreSearchHit> Hits,
    long TotalCount);
