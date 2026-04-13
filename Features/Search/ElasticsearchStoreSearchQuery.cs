using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Search;

public sealed class ElasticsearchStoreSearchQuery(
    IOptions<ElasticsearchStoreSearchOptions> options,
    IStoreSearchTextEmbeddingService embeddingService,
    ILogger<ElasticsearchStoreSearchQuery> logger)
    : IElasticsearchStoreSearchQuery
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;
    private readonly IStoreSearchTextEmbeddingService _embedding = embeddingService;
    private readonly ElasticsearchClient? _client = ElasticsearchStoreSearchClientFactory.TryCreate(options.Value);

    public bool IsConfigured => _client is not null;

    public async Task<ElasticsearchStoreSearchResult?> SearchAsync(
        string? name,
        string? category,
        IReadOnlyList<string> kinds,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return null;

        var nameQ = (name ?? "").Trim();
        var catQ = (category ?? "").Trim();
        var kindSet = kinds?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToArray()
            ?? Array.Empty<string>();
        if (kinds is not null && kindSet.Length == 0)
            return new ElasticsearchStoreSearchResult(Array.Empty<ElasticsearchStoreSearchHit>(), 0);

        float[]? queryVector = null;
        if (!string.IsNullOrEmpty(nameQ) && _opt.SemanticVectorDimensions > 0)
        {
            var textForEmbed = StoreSearchTextNormalize.FoldForMatch(nameQ);
            if (textForEmbed.Length > 0)
                queryVector = await _embedding.EmbedAsync(textForEmbed, cancellationToken);
            if (queryVector is { Length: > 0 } && queryVector.Length != _opt.SemanticVectorDimensions)
            {
                logger.LogWarning(
                    "Elasticsearch: vector de consulta con dimensión {Actual} distinta a SemanticVectorDimensions {Expected}; se omite kNN.",
                    queryVector.Length,
                    _opt.SemanticVectorDimensions);
                queryVector = null;
            }

            if (queryVector is { Length: > 0 } && !StoreSearchVectorMath.HasNonTrivialL2Norm(queryVector))
            {
                logger.LogDebug(
                    "Elasticsearch: vector de consulta con norma ~0 (cosine kNN no permitido); se omite kNN para «{Query}».",
                    nameQ.Length > 80 ? nameQ[..80] + "…" : nameQ);
                queryVector = null;
            }
        }

        SearchResponse<CatalogSearchDocument> response;
        try
        {
            response = await _client.SearchAsync<CatalogSearchDocument>(s =>
            {
                BuildSearchRequest(
                    s, nameQ, catQ,
                    kindSet,
                    hasDistanceFilter, userLat, userLng, maxKm,
                    skip, take,
                    queryVector);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Elasticsearch: error en búsqueda de catálogo; se usará fallback.");
            return null;
        }

        if (!response.IsValidResponse)
        {
            logger.LogWarning(
                "Elasticsearch: respuesta inválida en búsqueda: {Debug}",
                response.DebugInformation);
            return null;
        }

        var hits = ExtractHits(response, hasDistanceFilter);
        var totalCount = response.Total;
        return new ElasticsearchStoreSearchResult(hits, totalCount);
    }

    private void BuildSearchRequest(
        SearchRequestDescriptor<CatalogSearchDocument> s,
        string nameQ,
        string catQ,
        IReadOnlyList<string> kinds,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm,
        int skip,
        int take,
        float[]? queryVector)
    {
        s.Index(_opt.IndexName)
            .From(skip)
            .Size(take)
            .TrackTotalHits(new TrackHits(true));

        s.Query(q => BuildQuery(q, nameQ, catQ, kinds, hasDistanceFilter, userLat, userLng, maxKm));

        if (queryVector is { Length: > 0 } && StoreSearchVectorMath.HasNonTrivialL2Norm(queryVector))
        {
            var want = Math.Max(skip + take, 10);
            var numCandidates = Math.Clamp(want * 8, 100, 512);
            var k = Math.Min(numCandidates, want + 40);
            s.Knn(kn => kn
                .Field(new Field("nameSemanticVector"))
                .QueryVector(queryVector)
                .NumCandidates(numCandidates)
                .k(k)
                .Boost(0.4f));
        }

        if (hasDistanceFilter)
        {
            s.Sort(so => so.GeoDistance(g => g
                .Field(new Field(CatalogSearchDocument.ElasticsearchVtLocationField))
                .Order(SortOrder.Asc)
                .Unit(DistanceUnit.Kilometers)
                .DistanceType(GeoDistanceType.Arc)
                .Location(new[]
                {
                    GeoLocation.LatitudeLongitude(new LatLonGeoLocation { Lat = userLat, Lon = userLng }),
                })));
        }
        else
        {
            s.Sort(so => so.Field((CatalogSearchDocument d) => d.TrustScore, f => f.Order(SortOrder.Desc)));
            s.Sort(so => so.Field(new Field(CatalogSearchDocument.ElasticsearchVtCatalogSkField), f => f.Order(SortOrder.Asc)));
        }
    }

    private static QueryDescriptor<CatalogSearchDocument> BuildQuery(
        QueryDescriptor<CatalogSearchDocument> q,
        string nameQ,
        string catQ,
        IReadOnlyList<string> kinds,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm)
    {
        return q.Bool(b =>
        {
            var filters = new List<Action<QueryDescriptor<CatalogSearchDocument>>>(4);

            if (kinds.Count is > 0 and < 3)
            {
                filters.Add(f => f.Terms(t => t
                    .Field(fd => fd.Kind)
                    .Terms(new TermsQueryField(kinds.Select(k => (FieldValue)k).ToArray()))));
            }

            var hasName = !string.IsNullOrEmpty(nameQ);

            if (hasName)
            {
                b.MinimumShouldMatch(1);
                var shoulds = new List<Action<QueryDescriptor<CatalogSearchDocument>>>(10)
                {
                    s => s.Match(mt => mt
                        .Field(f => f.Name)
                        .Query(nameQ)
                        .Operator(Operator.And)
                        .Fuzziness(new Fuzziness("AUTO"))
                        .FuzzyTranspositions(true)
                        .Boost(2.5f)),
                    s => s.MultiMatch(mm => mm
                        .Fields(Fields.FromString("name^2,searchText,categories"))
                        .Query(nameQ)
                        .Type(TextQueryType.BestFields)
                        .Operator(Operator.And)),
                    s => s.MultiMatch(mm => mm
                        .Fields(Fields.FromString("name^2,searchText"))
                        .Query(nameQ)
                        .Type(TextQueryType.BestFields)
                        .Operator(Operator.And)
                        .Fuzziness(new Fuzziness("AUTO"))
                        .FuzzyTranspositions(true)
                        .Lenient(true)
                        .Boost(1.15f)),
                    s => s.MatchBoolPrefix(mbp => mbp
                        .Field(f => f.Name)
                        .Query(nameQ)
                        .MaxExpansions(50)),
                    s => s.MatchPhrase(mp => mp
                        .Field(f => f.Name)
                        .Query(nameQ)
                        .Slop(3)),
                    s => s.MatchPhrase(mp => mp
                        .Field(f => f.SearchText)
                        .Query(nameQ)
                        .Slop(5)),
                };

                // Fuzzy robusto vía query_string (Lucene). Útil si el cliente no serializa fuzziness como se espera.
                var qs = BuildLuceneFuzzyQueryString(nameQ);
                if (!string.IsNullOrEmpty(qs))
                {
                    shoulds.Add(s => s.QueryString(qsq => qsq
                        .Fields(Fields.FromString("name^2,searchText"))
                        .Query(qs)
                        .DefaultOperator(Operator.And)
                        .Boost(1.35f)));
                }

                var esc = StoreSearchWildcard.Escape(StoreSearchTextNormalize.FoldLowerKeyword(nameQ));
                if (!string.IsNullOrEmpty(esc))
                {
                    shoulds.Add(s => s.Wildcard(w => w
                        .Field(new Field(CatalogSearchDocument.ElasticsearchVtCatalogSkField))
                        .Value($"*{esc}*")
                        .CaseInsensitive(true)));
                }

                b.Should(shoulds.ToArray());
            }

            if (!string.IsNullOrEmpty(catQ))
            {
                var parts = catQ
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (parts.Length <= 1)
                {
                    var esc = StoreSearchWildcard.Escape(catQ);
                    filters.Add(f => f.Wildcard(w => w
                        .Field(fd => fd.Categories)
                        .Value($"*{esc}*")
                        .CaseInsensitive(true)));
                }
                else
                {
                    filters.Add(f => f.Bool(bb =>
                    {
                        bb.MinimumShouldMatch(1);
                        bb.Should(parts.Select(p =>
                        {
                            var esc = StoreSearchWildcard.Escape(p);
                            return (Action<QueryDescriptor<CatalogSearchDocument>>)(ss => ss.Wildcard(w => w
                                .Field(fd => fd.Categories)
                                .Value($"*{esc}*")
                                .CaseInsensitive(true)));
                        }).ToArray());
                    }));
                }
            }

            if (hasDistanceFilter)
            {
                filters.Add(f => f.GeoDistance(g => g
                    .Field(new Field(CatalogSearchDocument.ElasticsearchVtLocationField))
                    .Distance($"{maxKm}km")
                    .Location(GeoLocation.LatitudeLongitude(
                        new LatLonGeoLocation { Lat = userLat, Lon = userLng }))));
            }

            if (filters.Count > 0)
                b.Filter(filters.ToArray());

            if (!hasName && string.IsNullOrEmpty(catQ) && !hasDistanceFilter)
                b.Must(m => m.MatchAll(_ => { }));
        });
    }

    private static List<ElasticsearchStoreSearchHit> ExtractHits(SearchResponse<CatalogSearchDocument> response, bool hasDistanceFilter)
    {
        var hits = new List<ElasticsearchStoreSearchHit>();
        if (response.Hits is null)
            return hits;

        foreach (var hit in response.Hits)
        {
            var docId = hit.Id;
            if (string.IsNullOrEmpty(docId))
                continue;

            var src = hit.Source;
            var kind = src?.Kind ?? CatalogSearchKinds.Store;
            var storeId = src?.StoreId ?? "";
            if (string.IsNullOrEmpty(storeId))
                continue;

            var offerId = string.IsNullOrEmpty(src?.OfferId) ? null : src.OfferId;

            double? distKm = null;
            if (hasDistanceFilter && hit.Sort is not null)
            {
                foreach (var sv in hit.Sort)
                {
                    if (sv.TryGetDouble(out var d))
                    {
                        distKm = d;
                        break;
                    }
                }
            }

            hits.Add(new ElasticsearchStoreSearchHit(docId, kind, storeId, offerId, distKm));
        }

        return hits;
    }

    // (la norma se valida en StoreSearchVectorMath)

    private static string BuildLuceneFuzzyQueryString(string input)
    {
        // Mantener solo letras/dígitos/espacios; el resto puede romper query_string (o abrir la puerta a sintaxis no deseada).
        if (string.IsNullOrWhiteSpace(input))
            return "";

        Span<char> buf = stackalloc char[Math.Min(input.Length, 256)];
        var w = 0;
        foreach (var ch in input)
        {
            if (w >= buf.Length)
                break;
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                buf[w++] = ch;
        }

        var cleaned = new string(buf[..w]).Trim();
        if (cleaned.Length == 0)
            return "";

        var parts = cleaned
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .Select(t =>
            {
                // Para tokens cortos no aplicar fuzzy (ruido). Para 4+ usar distancia 1.
                if (t.Length < 4)
                    return t;
                return t + "~1";
            })
            .ToArray();

        return parts.Length == 0 ? "" : string.Join(' ', parts);
    }
}
