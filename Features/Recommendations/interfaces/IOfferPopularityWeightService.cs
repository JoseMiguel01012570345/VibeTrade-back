namespace VibeTrade.Backend.Features.Recommendations.Interfaces;

/// <summary>
/// Mantiene <see cref="StoreProductRow.PopularityWeight"/> / <see cref="StoreServiceRow.PopularityWeight"/>
/// alineado con interacciones + likes en ventana deslizante (30 días).
/// </summary>
public interface IOfferPopularityWeightService
{
    Task RecomputeAsync(string offerId, CancellationToken cancellationToken = default);

}
