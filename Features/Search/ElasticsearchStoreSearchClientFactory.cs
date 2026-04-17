using Elastic.Clients.Elasticsearch;

namespace VibeTrade.Backend.Features.Search;

internal static class ElasticsearchStoreSearchClientFactory
{
    public static ElasticsearchClient? TryCreate(ElasticsearchStoreSearchOptions opt)
    {
        if (!opt.Enabled || string.IsNullOrWhiteSpace(opt.Uri))
            return null;
        var uri = new Uri(opt.Uri.TrimEnd('/'));
        var settings = new ElasticsearchClientSettings(uri)
            .DefaultIndex(opt.IndexName)
            // Evita colgarse indefinidamente si el nodo no responde (SYN/HTTP sin límite).
            .RequestTimeout(TimeSpan.FromSeconds(30))
            .DisableDirectStreaming()
            .PrettyJson();
        return new ElasticsearchClient(settings);
    }
}
