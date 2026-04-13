using System.Net.Http;
using System.Net.Sockets;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Search;

public sealed class ElasticsearchStoreSearchIndexWriter(
    AppDbContext db,
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<ElasticsearchStoreSearchIndexWriter> logger)
    : IStoreSearchIndexWriter
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;
    private readonly ElasticsearchClient? _client = ElasticsearchStoreSearchClientFactory.TryCreate(options.Value);

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        try
        {
            var exists = await _client.Indices.ExistsAsync(_opt.IndexName, cancellationToken);
            if (!exists.IsValidResponse)
            {
                logger.LogWarning(
                    "Elasticsearch: no se pudo consultar el índice {Index} en {Uri}. ¿Está el cluster en marcha? {Debug}",
                    _opt.IndexName,
                    _opt.Uri ?? "(sin uri)",
                    exists.DebugInformation);
                return;
            }

            if (exists.Exists)
            {
                await TryEnsureNameSortMappingAsync(cancellationToken);
                return;
            }

            var create = await _client.Indices.CreateAsync(_opt.IndexName, c => c
                .Mappings(m => m
                    .Properties<StoreSearchDocument>(p => p
                        .Keyword(s => s.StoreId)
                        .Text(t => t.Name)
                        .Keyword(k => k.NameSort, k2 => k2.IgnoreAbove(256))
                        .Keyword(k => k.Categories)
                        .GeoPoint(g => g.Location)
                        .LongNumber(n => n.TrustScore)
                        .LongNumber(n => n.PublishedProducts)
                        .LongNumber(n => n.PublishedServices))), cancellationToken);

            if (!create.IsValidResponse)
            {
                logger.LogWarning(
                    "Elasticsearch: no se pudo crear el índice {Index} en {Uri}: {Debug}",
                    _opt.IndexName,
                    _opt.Uri ?? "(sin uri)",
                    create.DebugInformation);
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            || ex is SocketException
            || ex.InnerException is SocketException)
        {
            logger.LogWarning(
                ex,
                "Elasticsearch: sin conexión al preparar el índice {Index} ({Uri}). Activá el nodo o deshabilitá Elasticsearch:Enabled.",
                _opt.IndexName,
                _opt.Uri ?? "(sin uri)");
        }
    }

    /// <summary>
    /// Índices creados antes de <c>nameSort</c> no tenían subcampo <c>name.sort</c>; mergeamos el mapping para poder ordenar.
    /// </summary>
    private async Task TryEnsureNameSortMappingAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        var put = await _client.Indices.PutMappingAsync(_opt.IndexName, p => p
            .Properties<StoreSearchDocument>(pr => pr
                .Keyword(k => k.NameSort, k2 => k2.IgnoreAbove(256))), cancellationToken);

        if (!put.IsValidResponse)
        {
            logger.LogWarning(
                "Elasticsearch: no se pudo actualizar mapping nameSort en {Index}: {Debug}",
                _opt.IndexName,
                put.DebugInformation);
        }
    }

    public async Task UpsertStoresAsync(IReadOnlyCollection<string> storeIds, CancellationToken cancellationToken = default)
    {
        if (_client is null || storeIds.Count == 0)
            return;

        var ops = new BulkOperationsCollection();
        foreach (var id in storeIds)
        {
            var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (store is null)
            {
                ops.Add(new BulkDeleteOperation<StoreSearchDocument>(id) { Index = _opt.IndexName });
                continue;
            }

            var doc = await StoreSearchDocumentFactory.FromStoreAsync(db, store, cancellationToken);
            if (doc is null)
                continue;
            ops.Add(new BulkIndexOperation<StoreSearchDocument>(doc) { Index = _opt.IndexName, Id = doc.StoreId });
        }

        if (ops.Count == 0)
            return;

        var bulk = await _client.BulkAsync(new BulkRequest(_opt.IndexName) { Operations = ops }, cancellationToken);
        if (!bulk.IsValidResponse)
            logger.LogWarning(
                "Elasticsearch: bulk upsert parcial o con errores: {Debug}",
                bulk.DebugInformation);
    }

    public async Task ReindexAllStoresAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        var ids = await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken);
        const int chunk = 200;
        for (var i = 0; i < ids.Count; i += chunk)
        {
            var slice = ids.Skip(i).Take(chunk).ToList();
            await UpsertStoresAsync(slice, cancellationToken);
        }

        logger.LogInformation("Elasticsearch: reindexadas {Count} tiendas.", ids.Count);
    }
}
