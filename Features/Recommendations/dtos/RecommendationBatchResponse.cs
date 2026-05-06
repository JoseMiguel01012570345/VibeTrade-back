using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Features.Recommendations.Dtos;

/// <summary>
/// Lote de recomendaciones. <see cref="OfferIds"/> conserva el orden del ranking; <see cref="Offers"/>
/// una entrada por id; <see cref="StoreBadges"/> alinea con <c>market.stores[*]</c>.
/// </summary>
public sealed record RecommendationBatchResponse(
    string[] OfferIds,
    IReadOnlyDictionary<string, HomeOfferViewDto> Offers,
    IReadOnlyDictionary<string, StoreProfileWorkspaceData> StoreBadges,
    int BatchSize,
    double Threshold)
{
    public static RecommendationBatchResponse Empty(int batchSize, double threshold) =>
        new(
            Array.Empty<string>(),
            new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal),
            new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal),
            Math.Max(1, batchSize),
            threshold);
}
