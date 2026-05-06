namespace VibeTrade.Backend.Features.Search.Interfaces;

public interface IStoreSearchIndexWriter
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Actualiza o inserta documentos para los ids indicados (carga desde la base).</summary>
    Task UpsertStoresAsync(IReadOnlyCollection<string> storeIds, CancellationToken cancellationToken = default);

    /// <summary>Reindexa todas las tiendas (bulk).</summary>
    Task ReindexAllStoresAsync(CancellationToken cancellationToken = default);
}
