namespace VibeTrade.Backend.Features.Search.Interfaces;

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
