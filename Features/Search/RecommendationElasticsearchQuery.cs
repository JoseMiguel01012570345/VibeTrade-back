using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Search;

public sealed class RecommendationElasticsearchQuery(
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<RecommendationElasticsearchQuery> logger)
    : IRecommendationElasticsearchQuery
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;
    private readonly ElasticsearchClient? _client = ElasticsearchStoreSearchClientFactory.TryCreate(options.Value);

    public bool IsConfigured => _client is not null;

    public async Task<IReadOnlyList<RecommendationElasticsearchHit>> SearchOffersAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return Array.Empty<RecommendationElasticsearchHit>();

        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return Array.Empty<RecommendationElasticsearchHit>();

        var effectiveTake = Math.Clamp(take * 4, take, 400);

        SearchResponse<CatalogSearchDocument> response;
        try
        {
            response = await _client.SearchAsync<CatalogSearchDocument>(s => s
                .Index(_opt.IndexName)
                .Size(effectiveTake)
                .Query(rq => rq.Bool(b =>
                {
                    b.Must(m => m.MultiMatch(mm => mm
                        .Fields(Fields.FromString("name^2,searchText,categories"))
                        .Query(q)
                        .Type(TextQueryType.BestFields)
                        .Operator(Operator.Or)));
                    b.Filter(f => f.Terms(t => t
                        .Field(fd => fd.Kind)
                        .Terms(new TermsQueryField(new FieldValue[]
                        {
                            CatalogSearchKinds.Product,
                            CatalogSearchKinds.Service,
                        }))));
                })), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Elasticsearch: error en búsqueda de recomendaciones.");
            return Array.Empty<RecommendationElasticsearchHit>();
        }

        if (!response.IsValidResponse || response.Hits is null)
        {
            logger.LogWarning(
                "Elasticsearch: respuesta inválida en recomendaciones: {Debug}",
                response.DebugInformation);
            return Array.Empty<RecommendationElasticsearchHit>();
        }

        var list = new List<RecommendationElasticsearchHit>(effectiveTake);
        foreach (var hit in response.Hits)
        {
            var src = hit.Source;
            if (src is null)
                continue;
            var oid = (src.OfferId ?? "").Trim();
            if (oid.Length == 0)
                continue;
            var kind = string.IsNullOrEmpty(src.Kind) ? CatalogSearchKinds.Product : src.Kind;
            var score = hit.Score ?? 0d;
            list.Add(new RecommendationElasticsearchHit(oid, score, kind));
        }

        list.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (list.Count > take)
            list.RemoveRange(take, list.Count - take);
        return list;
    }
}
