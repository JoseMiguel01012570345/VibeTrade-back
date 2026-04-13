using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Search;

public sealed class ElasticsearchStoreSearchQuery(
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<ElasticsearchStoreSearchQuery> logger)
    : IElasticsearchStoreSearchQuery
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;
    private readonly ElasticsearchClient? _client = ElasticsearchStoreSearchClientFactory.TryCreate(options.Value);

    public bool IsConfigured => _client is not null;

    public async Task<ElasticsearchStoreSearchResult?> SearchAsync(
        string? name,
        string? category,
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

        SearchResponse<StoreSearchDocument> response;
        try
        {
            response = await _client.SearchAsync<StoreSearchDocument>(s =>
            {
                BuildSearchRequest(
                    s, nameQ, catQ,
                    hasDistanceFilter, userLat, userLng, maxKm,
                    skip, take);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Elasticsearch: error en búsqueda de tiendas; se usará fallback.");
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
        SearchRequestDescriptor<StoreSearchDocument> s,
        string nameQ,
        string catQ,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm,
        int skip,
        int take)
    {
        s.Index(_opt.IndexName)
         .From(skip)
         .Size(take)
         .TrackTotalHits(new TrackHits(true));

        s.Query(q => BuildQuery(q, nameQ, catQ, hasDistanceFilter, userLat, userLng, maxKm));
        
        if (hasDistanceFilter)
        {
            s.Sort(so => so.GeoDistance(g => g
                .Field(f => f.Location)
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
            s.Sort(so => so.Field((StoreSearchDocument d) => d.TrustScore, f => f.Order(SortOrder.Desc)));
            s.Sort(so => so.Field((StoreSearchDocument d) => d.NameSort, f => f.Order(SortOrder.Asc)));
        }
    }

    private QueryDescriptor<StoreSearchDocument> BuildQuery(
        QueryDescriptor<StoreSearchDocument> q,
        string nameQ,
        string catQ,
        bool hasDistanceFilter,
        double userLat,
        double userLng,
        double maxKm)
    {
        return q.Bool(b =>
        {
            if (!string.IsNullOrEmpty(nameQ))
            {
                b.Must(m => m.Match(mt => mt
                    .Field(f => f.Name)
                    .Query(nameQ)
                    .Operator(Operator.And)));
            }

            if (!string.IsNullOrEmpty(catQ))
            {
                var esc = StoreSearchWildcard.Escape(catQ);
                b.Filter(f => f.Wildcard(w => w
                    .Field(fd => fd.Categories)
                    .Value($"*{esc}*")
                    .CaseInsensitive(true)));
            }

            if (hasDistanceFilter)
            {
                b.Filter(f => f.GeoDistance(g => g
                    .Field(fd => fd.Location)
                    .Distance($"{maxKm}km")
                    .Location(GeoLocation.LatitudeLongitude(
                        new LatLonGeoLocation { Lat = userLat, Lon = userLng }))));
            }

            if (string.IsNullOrEmpty(nameQ) && string.IsNullOrEmpty(catQ) && !hasDistanceFilter)
                b.Must(m => m.MatchAll(_ => { }));
        });
    }

    private List<ElasticsearchStoreSearchHit> ExtractHits(SearchResponse<StoreSearchDocument> response, bool hasDistanceFilter)
    {
        var hits = new List<ElasticsearchStoreSearchHit>();
        if (response.Hits is not null)
        {
            foreach (var hit in response.Hits)
            {
                var id = hit.Id ?? hit.Source?.StoreId;
                if (string.IsNullOrEmpty(id))
                    continue;
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

                hits.Add(new ElasticsearchStoreSearchHit(id, distKm));
            }
        }
        return hits;
    }
}
