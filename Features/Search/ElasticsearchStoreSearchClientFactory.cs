using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

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
            // Captura request/response en DebugInformation (útil para bulk errores)
            .DisableDirectStreaming()
            .PrettyJson();

        // Auth: prefer API key over basic auth.
        if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        {
            settings = settings.Authentication(new ApiKey(opt.ApiKey));
        }
        else if (!string.IsNullOrWhiteSpace(opt.Username) && !string.IsNullOrWhiteSpace(opt.Password))
        {
            settings = settings.Authentication(new BasicAuthentication(opt.Username, opt.Password));
        }

        return new ElasticsearchClient(settings);
    }
}
