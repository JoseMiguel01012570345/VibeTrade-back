namespace VibeTrade.Backend.Features.Search.Interfaces;

/// <summary>Consultas Elasticsearch orientadas al feed de recomendaciones (scores léxicos).</summary>
public interface IRecommendationElasticsearchQuery
{
    bool IsConfigured { get; }

    /// <summary>
    /// Búsqueda multi_match sobre nombre y texto indexado; solo productos/servicios (kind).
    /// El filtro de ofertas del viewer se aplica en base de datos sobre los ids devueltos.
    /// </summary>
    Task<IReadOnlyList<RecommendationElasticsearchHit>> SearchOffersAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default);
}
