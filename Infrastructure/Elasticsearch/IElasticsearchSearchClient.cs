using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;
using VibeTrade.Backend.Features.Search.Dtos;

namespace VibeTrade.Backend.Infrastructure.Elasticsearch;

public interface IElasticsearchSearchClient
{
    bool IsConfigured { get; }

    ElasticsearchClient? Client { get; }

    string IndexName { get; }
}

public sealed class ElasticsearchSearchClient(IOptions<ElasticsearchStoreSearchOptions> options)
    : IElasticsearchSearchClient
{
    private readonly ElasticsearchStoreSearchOptions _opt = options.Value;

    public bool IsConfigured => Client is not null;

    public ElasticsearchClient? Client { get; } = ElasticsearchSearchClientFactory.TryCreate(options.Value);

    public string IndexName => _opt.IndexName;
}

internal static class ElasticsearchSearchClientFactory
{
    /// <summary>Integración: Testcontainers ES con TLS autofirmado (véase docs del módulo).</summary>
    private const string SkipCertValidationEnv = "VIBETRADE_ELASTICSEARCH_SKIP_CERT_VALIDATION";

    public static ElasticsearchClient? TryCreate(ElasticsearchStoreSearchOptions opt)
    {
        if (!opt.Enabled || string.IsNullOrWhiteSpace(opt.Uri))
            return null;
        var uri = new Uri(opt.Uri.TrimEnd('/'));
        var settings = new ElasticsearchClientSettings(uri)
            .DefaultIndex(opt.IndexName)
            .RequestTimeout(TimeSpan.FromSeconds(30))
            .DisableDirectStreaming()
            .PrettyJson();

        if (string.Equals(Environment.GetEnvironmentVariable(SkipCertValidationEnv), "1", StringComparison.Ordinal))
            settings.ServerCertificateValidationCallback((_, _, _, _) => true);

        return new ElasticsearchClient(settings);
    }
}
