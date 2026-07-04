using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Market.Interfaces;

public interface IStoreCatalogSearchService
{
    /// <summary>
    /// Busca productos y servicios publicados de una tienda con <c>ILIKE</c> en PostgreSQL.
    /// Devuelve <c>null</c> si la tienda no existe.
    /// </summary>
    Task<StoreCatalogSearchResponse?> SearchPublishedCatalogAsync(
        string storeId,
        string? query,
        CancellationToken cancellationToken = default);
}
