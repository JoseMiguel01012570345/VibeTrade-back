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

    /// <summary>
    /// Sugerencias de texto (nombres de productos/servicios publicados) con <c>ILIKE</c>.
    /// Devuelve <c>null</c> si la tienda no existe.
    /// </summary>
    Task<StoreAutocompleteResponse?> AutocompletePublishedCatalogAsync(
        string storeId,
        string? query,
        int? limit,
        CancellationToken cancellationToken = default);
}
