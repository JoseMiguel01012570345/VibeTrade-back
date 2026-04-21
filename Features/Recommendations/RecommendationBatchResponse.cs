using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Lote de recomendaciones. <see cref="OfferIds"/> conserva el orden del ranking (y repeticiones si el feed las emite);
/// <see cref="Offers"/> solo puede tener una entrada por id (JSON). <see cref="StoreBadges"/> alinea con <c>market.stores[*]</c>.
/// </summary>
public sealed record RecommendationBatchResponse(
    string[] OfferIds,
    JsonObject Offers,
    JsonObject StoreBadges,
    int BatchSize,
    double Threshold)
{
    public static RecommendationBatchResponse Empty(int batchSize, double threshold) =>
        new(
            Array.Empty<string>(),
            new JsonObject(),
            new JsonObject(),
            Math.Max(1, batchSize),
            threshold);
}
