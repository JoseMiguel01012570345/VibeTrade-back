namespace VibeTrade.Backend.Features.Recommendations.Interfaces;

/// <summary>
/// Mantiene <see cref="Data.Entities.StoreProductRow.PopularityWeight"/> / <see cref="Data.Entities.StoreServiceRow.PopularityWeight"/>
/// alineado con interacciones + likes en ventana deslizante (30 días).
/// </summary>
public interface IOfferPopularityWeightService
{
    Task RecomputeAsync(string offerId, CancellationToken cancellationToken = default);

}
