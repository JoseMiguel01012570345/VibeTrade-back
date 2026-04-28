namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Mantiene <see cref="Data.Entities.StoreProductRow.PopularityWeight"/> / <see cref="Data.Entities.StoreServiceRow.PopularityWeight"/>
/// alineado con interacciones + likes en ventana deslizante (30 días).
/// </summary>
public interface IOfferPopularityWeightService
{
    Task RecomputeAsync(string offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalcula todas las ofertas publicadas (una agregación global + actualización por filas). Pensado para arranque o jobs.
    /// </summary>
    Task RecomputeAllPublishedAsync(CancellationToken cancellationToken = default);
}
