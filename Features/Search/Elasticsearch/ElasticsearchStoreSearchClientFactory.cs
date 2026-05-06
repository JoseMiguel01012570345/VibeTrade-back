using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace VibeTrade.Backend.Features.Search.Elasticsearch;

internal static class ElasticsearchStoreSearchClientFactory
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
            // Evita colgarse indefinidamente si el nodo no responde (SYN/HTTP sin límite).
            .RequestTimeout(TimeSpan.FromSeconds(30))
            .DisableDirectStreaming()
            .PrettyJson();

        if (string.Equals(Environment.GetEnvironmentVariable(SkipCertValidationEnv), "1", StringComparison.Ordinal))
            settings.ServerCertificateValidationCallback(CertificateValidations.AllowAll);

        return new ElasticsearchClient(settings);
    }
}
