using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Lote de recomendaciones. <see cref="StoreBadges"/> repite la forma de <c>market.stores[*]</c> para refrescar URL y metadatos en el cliente.
/// </summary>
public sealed record RecommendationBatchResponse(
    IReadOnlyList<string> OfferIds,
    JsonObject Offers,
    int NextCursor,
    int TotalAvailable,
    int BatchSize,
    double Threshold,
    bool Wrapped,
    IReadOnlyList<string> RecommendedStoreIds,
    JsonObject StoreBadges)
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
            Array.Empty<string>(),
            new JsonObject());
}
