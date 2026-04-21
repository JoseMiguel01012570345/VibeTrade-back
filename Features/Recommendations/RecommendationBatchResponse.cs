using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Lote de recomendaciones. Las ofertas van en <see cref="Offers"/>; el cliente toma el orden por las claves del objeto.
/// <see cref="StoreBadges"/> repite la forma de <c>market.stores[*]</c> para refrescar URL y metadatos.
/// </summary>
public sealed record RecommendationBatchResponse(
    JsonObject Offers,
    JsonObject StoreBadges,
    int BatchSize,
    double Threshold)
{
    public static RecommendationBatchResponse Empty(int batchSize, double threshold) =>
        new(
            new JsonObject(),
            new JsonObject(),
            Math.Max(1, batchSize),
            threshold);
}
