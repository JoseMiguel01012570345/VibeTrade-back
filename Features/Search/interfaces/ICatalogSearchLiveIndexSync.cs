namespace VibeTrade.Backend.Features.Search.Interfaces;

/// <summary>
/// Sincroniza el índice de búsqueda de catálogo (Elasticsearch) con el estado actual en base de datos
/// para una o más tiendas, tras cambios en tienda/producto/servicio u ofertas emergentes.
/// </summary>
public interface ICatalogSearchLiveIndexSync
{
    /// <summary>Reindexa la tienda indicada (store + productos + servicios + emergentes activos).</summary>
    Task SyncStoreAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>Reindexa varias tiendas en lote.</summary>
    Task SyncStoresAsync(
        IReadOnlyCollection<string> storeIds,
        CancellationToken cancellationToken = default);
}
