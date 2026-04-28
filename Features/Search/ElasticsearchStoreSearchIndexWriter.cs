using System.Net.Http;
using System.Net.Sockets;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Search;

/// <summary>
/// Escritura al índice de catálogo en Elasticsearch.
/// Si un índice previo tiene campos con tipo distinto (p.ej. <c>vtCatalogSk</c> como <c>text</c>),
/// ES no permite <c>PUT _mapping</c> para corregirlo: hay que borrar el índice o usar otro <see cref="ElasticsearchStoreSearchOptions.IndexName"/>.
/// </summary>
public sealed class ElasticsearchStoreSearchIndexWriter(
    AppDbContext db,
    IOptions<ElasticsearchStoreSearchOptions> options,
    IStoreSearchTextEmbeddingService embeddingService,
    ILogger<ElasticsearchStoreSearchIndexWriter> logger)
    : IStoreSearchIndexWriter
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;
    private readonly IStoreSearchTextEmbeddingService _embedding = embeddingService;
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
                await TryEnsureCatalogMappingAsync(cancellationToken);
                return;
            }

            var create = await _client.Indices.CreateAsync(_opt.IndexName, c => c
                .Mappings(m => m
                    .Properties<CatalogSearchDocument>(p =>
                    {
                        p.Keyword(k => k.Kind)
                            .Keyword(k => k.StoreId)
                            .Keyword(k => k.OfferId, k2 => k2.IgnoreAbove(128))
                            .Keyword(k => k.VtCatalogSk, k2 => k2.IgnoreAbove(512))
                            .Keyword(k => k.Categories)
                            .Text(t => t.Name)
                            .Text(t => t.SearchText)
                            .GeoPoint(g => g.Location)
                            .GeoPoint(g => g.VtGeoPoint)
                            .IntegerNumber(n => n.TrustScore)
                            .LongNumber(n => n.PublishedProducts)
                            .LongNumber(n => n.PublishedServices);
                        if (_opt.SemanticVectorDimensions > 0)
                        {
                            p.DenseVector(d => d.NameSemanticVector, dv => dv
                                .Dims(_opt.SemanticVectorDimensions)
                                .Similarity(DenseVectorSimilarity.Cosine)
                                .Index(true));
                        }
                    })), cancellationToken);

            if (!create.IsValidResponse)
            {
                logger.LogWarning(
                    "Elasticsearch: no se pudo crear el índice {Index} en {Uri}: {Debug}",
                    _opt.IndexName,
                    _opt.Uri ?? "(sin uri)",
                    create.DebugInformation);
            }
            else
            {
                await PutVtCatalogSkKeywordMappingAsync(cancellationToken);
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
    /// ES rechaza cambiar el tipo de un campo existente (p.ej. text → keyword).
    /// </summary>
    private static bool IsImmutableElasticsearchMappingConflict(string? debugInformation)
    {
        if (string.IsNullOrEmpty(debugInformation))
            return false;
        return debugInformation.Contains("cannot be changed from type", StringComparison.OrdinalIgnoreCase)
            || (debugInformation.Contains("illegal_argument_exception", StringComparison.OrdinalIgnoreCase)
                && debugInformation.Contains("mapper", StringComparison.OrdinalIgnoreCase)
                && debugInformation.Contains("cannot be changed", StringComparison.OrdinalIgnoreCase));
    }

    private void LogPutMappingOutcome(
        string fieldForLog,
        bool isValidResponse,
        string? debugInformation,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (isValidResponse)
            return;

        if (IsImmutableElasticsearchMappingConflict(debugInformation))
        {
            logger.LogInformation(
                "Elasticsearch: el índice {Index} ya tiene el campo {Field} con un tipo distinto al esperado; no se puede corregir con PUT mapping. " +
                "Eliminá ese índice en el cluster (DELETE con el mismo nombre) y reiniciá la app, o usá otro valor en la opción de configuración IndexName y reindexá el catálogo.",
                _opt.IndexName,
                fieldForLog);
            return;
        }

        logger.LogWarning(
            "Elasticsearch: no se pudo registrar el mapping de «{Field}» en «{Index}»: {Debug}",
            fieldForLog,
            _opt.IndexName,
            debugInformation ?? "(sin detalle)");
    }

    private async Task PutVtCatalogSkKeywordMappingAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        var put = await _client.Indices.PutMappingAsync(new PutMappingRequest(_opt.IndexName)
        {
            Properties = new Properties
            {
                { CatalogSearchDocument.ElasticsearchVtCatalogSkField, new KeywordProperty { IgnoreAbove = 512 } },
            },
        }, cancellationToken);

        LogPutMappingOutcome(CatalogSearchDocument.ElasticsearchVtCatalogSkField, put.IsValidResponse, put.DebugInformation, cancellationToken);
    }

    private async Task PutNameSemanticVectorMappingAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;
        if (_opt.SemanticVectorDimensions <= 0)
            return;

        var put = await _client.Indices.PutMappingAsync(new PutMappingRequest(_opt.IndexName)
        {
            Properties = new Properties
            {
                {
                    "nameSemanticVector",
                    new DenseVectorProperty
                    {
                        Dims = _opt.SemanticVectorDimensions,
                        Similarity = DenseVectorSimilarity.Cosine,
                        Index = true,
                    }
                },
            },
        }, cancellationToken);

        LogPutMappingOutcome("nameSemanticVector", put.IsValidResponse, put.DebugInformation, cancellationToken);
    }

    private async Task PutVtGeoPointMappingAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        var put = await _client.Indices.PutMappingAsync(new PutMappingRequest(_opt.IndexName)
        {
            Properties = new Properties
            {
                { CatalogSearchDocument.ElasticsearchVtGeoPointField, new GeoPointProperty() },
            },
        }, cancellationToken);

        LogPutMappingOutcome(CatalogSearchDocument.ElasticsearchVtGeoPointField, put.IsValidResponse, put.DebugInformation, cancellationToken);
    }

    private async Task TryEnsureCatalogMappingAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        // En índices ya existentes, evitar re-declarar mappings completos:
        // ES no permite cambiar el tipo de un campo (p.ej. offerId text -> keyword),
        // y un PutMapping con todo el POCO puede bloquear el reindex con errores repetidos.
        // Acá solo aseguramos campos que agregamos “a futuro” (vtCatalogSk, vtGeoPoint y el vector).
        await PutVtCatalogSkKeywordMappingAsync(cancellationToken);
        await PutVtGeoPointMappingAsync(cancellationToken);
        await PutNameSemanticVectorMappingAsync(cancellationToken);
    }

    public async Task UpsertStoresAsync(IReadOnlyCollection<string> storeIds, CancellationToken cancellationToken = default)
    {
        if (_client is null || storeIds.Count == 0)
            return;

        foreach (var storeId in storeIds)
            await UpsertOneStoreTreeAsync(storeId, cancellationToken);
    }

    private async Task UpsertOneStoreTreeAsync(string storeId, CancellationToken cancellationToken)
    {
        if (_client is null)
            return;

        var del = await _client.DeleteByQueryAsync<CatalogSearchDocument>(d => d
            .Indices(_opt.IndexName)
            .Query(q => q.Term(t => t
                .Field(f => f.StoreId)
                .Value(storeId))), cancellationToken);

        if (!del.IsValidResponse)
        {
            logger.LogWarning(
                "Elasticsearch: deleteByQuery storeId={StoreId} en {Index}: {Debug}",
                storeId,
                _opt.IndexName,
                del.DebugInformation);
        }

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (store is null)
            return;

        var pubProducts = await db.StoreProducts.AsNoTracking()
            .CountAsync(p => p.StoreId == storeId && p.Published, cancellationToken);
        var pubServices = await db.StoreServices.AsNoTracking()
            .CountAsync(s => s.StoreId == storeId && (s.Published == null || s.Published == true), cancellationToken);

        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.Published)
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => s.StoreId == storeId && (s.Published == null || s.Published == true))
            .ToListAsync(cancellationToken);

        var ops = new BulkOperationsCollection();

        var storeDoc = CatalogSearchDocumentFactory.FromStore(store, pubProducts, pubServices);
        await AttachVectorAsync(storeDoc, storeDoc.SearchText, cancellationToken);
        ops.Add(new BulkIndexOperation<CatalogSearchDocument>(storeDoc)
        {
            Index = _opt.IndexName,
            Id = CatalogSearchIds.Store(storeId),
        });

        foreach (var p in products)
        {
            var doc = CatalogSearchDocumentFactory.FromProduct(p, store, pubProducts, pubServices);
            if (doc is null)
                continue;
            await AttachVectorAsync(doc, doc.SearchText, cancellationToken);
            ops.Add(new BulkIndexOperation<CatalogSearchDocument>(doc)
            {
                Index = _opt.IndexName,
                Id = CatalogSearchIds.Product(p.Id),
            });
        }

        foreach (var s in services)
        {
            var doc = CatalogSearchDocumentFactory.FromService(s, store, pubProducts, pubServices);
            if (doc is null)
                continue;
            await AttachVectorAsync(doc, doc.SearchText, cancellationToken);
            ops.Add(new BulkIndexOperation<CatalogSearchDocument>(doc)
            {
                Index = _opt.IndexName,
                Id = CatalogSearchIds.Service(s.Id),
            });
        }

        // Hojas de ruta publicadas (<c>emo_*</c>): mismo criterio que el JSON de oferta (base puede estar despublicada).
        var emergents = await db.EmergentOffers.AsNoTracking()
            .Where(e =>
                e.RetractedAtUtc == null
                && (string.IsNullOrEmpty(e.Kind)
                    || e.Kind == EmergentRouteOfferRanking.EmergentKindRouteSheet))
            .Where(e =>
                db.StoreProducts.Any(p => p.Id == e.OfferId && p.StoreId == storeId) ||
                db.StoreServices.Any(sv => sv.Id == e.OfferId && sv.StoreId == storeId) ||
                db.Stores.Any(st => st.Id == storeId && st.OwnerUserId == e.PublisherUserId))
            .ToListAsync(cancellationToken);

        if (emergents.Count > 0)
        {
            var emBaseIds = emergents
                .Select(e => e.OfferId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            var emProductsById = emBaseIds.Count == 0
                ? new Dictionary<string, StoreProductRow>(StringComparer.Ordinal)
                : await db.StoreProducts.AsNoTracking()
                    .Where(p => emBaseIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, cancellationToken);
            var emServicesById = emBaseIds.Count == 0
                ? new Dictionary<string, StoreServiceRow>(StringComparer.Ordinal)
                : await db.StoreServices.AsNoTracking()
                    .Where(sv => emBaseIds.Contains(sv.Id))
                    .ToDictionaryAsync(sv => sv.Id, cancellationToken);

            foreach (var e in emergents)
            {
                StoreProductRow? emP = null;
                StoreServiceRow? emS = null;
                if (!string.IsNullOrEmpty(e.OfferId))
                {
                    emProductsById.TryGetValue(e.OfferId, out emP);
                    if (emP is null)
                        emServicesById.TryGetValue(e.OfferId, out emS);
                }

                if (emP is not null)
                {
                    if (!string.Equals(emP.StoreId, storeId, StringComparison.Ordinal))
                        continue;
                }
                else if (emS is not null)
                {
                    if (!string.Equals(emS.StoreId, storeId, StringComparison.Ordinal))
                        continue;
                }
                else if (!string.Equals(store.OwnerUserId, e.PublisherUserId, StringComparison.Ordinal))
                {
                    continue;
                }

                var emDoc = CatalogSearchDocumentFactory.FromEmergent(e, store, emP, emS, pubProducts, pubServices);
                if (emDoc is null)
                    continue;
                await AttachVectorAsync(emDoc, emDoc.SearchText, cancellationToken);
                ops.Add(new BulkIndexOperation<CatalogSearchDocument>(emDoc)
                {
                    Index = _opt.IndexName,
                    Id = CatalogSearchIds.Emergent(e.Id),
                });
            }
        }

        if (ops.Count == 0)
            return;

        var bulk = await _client.BulkAsync(new BulkRequest(_opt.IndexName) { Operations = ops }, cancellationToken);
        if (!bulk.IsValidResponse)
        {
            var errors = new List<string>(8);
            var shown = 0;
            if (bulk.Items is not null)
            {
                foreach (var it in bulk.Items)
                {
                    if (it is null || it.Error is null)
                        continue;
                    shown++;
                    if (shown <= 8)
                    {
                        errors.Add(
                            $"op={it.Operation} id={it.Id ?? "(sin id)"} status={it.Status} type={it.Error.Type} reason={it.Error.Reason}");
                    }
                    else
                    {
                        break;
                    }
                }
            }

            logger.LogWarning(
                "Elasticsearch: bulk catálogo parcial o con errores (muestras: {SampleCount}): {Samples}\n{Debug}",
                errors.Count,
                string.Join(" | ", errors),
                bulk.DebugInformation);
        }
    }

    private async Task AttachVectorAsync(CatalogSearchDocument doc, string embText, CancellationToken cancellationToken)
    {
        if (_opt.SemanticVectorDimensions <= 0)
            return;
        var vec = await _embedding.EmbedAsync(embText, cancellationToken);
        if (vec is { Length: > 0 } && StoreSearchVectorMath.HasNonTrivialL2Norm(vec))
            doc.NameSemanticVector = vec;
    }

    public async Task ReindexAllStoresAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        var ids = await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken);
        const int chunk = 50;
        for (var i = 0; i < ids.Count; i += chunk)
        {
            var slice = ids.Skip(i).Take(chunk).ToList();
            await UpsertStoresAsync(slice, cancellationToken);
        }

        logger.LogInformation("Elasticsearch: reindexado catálogo para {Count} tiendas.", ids.Count);
    }
}
