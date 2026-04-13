using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Recommendations;

public sealed record RecommendationBatchResponse(
    IReadOnlyList<string> OfferIds,
    JsonObject Offers,
    int NextCursor,
    int TotalAvailable,
    int BatchSize,
    double Threshold,
    bool Wrapped,
    IReadOnlyList<string> RecommendedStoreIds)
{
    public static RecommendationBatchResponse Empty(int batchSize, double threshold) =>
        new(
            Array.Empty<string>(),
            new JsonObject(),
            0,
            0,
            Math.Max(1, batchSize),
            threshold,
            false,
            Array.Empty<string>());
}
