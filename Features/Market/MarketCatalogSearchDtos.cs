using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Market;

public sealed record CatalogSearchItem(
    string Kind,
    JsonObject Store,
    JsonObject? Offer,
    long? PublishedProducts,
    long? PublishedServices,
    double? DistanceKm);

public sealed record StoreSearchResponse(
    IReadOnlyList<CatalogSearchItem> Items,
    bool hasMore,
    int Offset,
    int Limit);

public sealed record StoreAutocompleteResponse(IReadOnlyList<string> Suggestions);
