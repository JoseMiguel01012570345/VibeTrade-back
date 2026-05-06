using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Search.Elasticsearch;

/// <summary>
/// Crea/actualiza el índice de búsqueda y opcionalmente reindexa <b>después</b> de que Kestrel ya escucha.
/// Así un Elasticsearch lento o inalcanzable no bloquea el arranque de la API (evita peticiones HTTP «pending» para siempre, p. ej. <c>GET /api/v1/bootstrap/guest</c>).
/// </summary>
public sealed class ElasticsearchSearchStartupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<ElasticsearchSearchStartupHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var esCfg = options.Value;
        if (!esCfg.Enabled || string.IsNullOrWhiteSpace(esCfg.Uri))
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<IStoreSearchIndexWriter>();
            await writer.EnsureIndexAsync(stoppingToken);
            if (esCfg.ReindexOnStartup)
                await writer.ReindexAllStoresAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            /* apagado del host */
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Elasticsearch: no se pudo preparar el índice en segundo plano; la búsqueda de catálogo seguirá con fallback hasta que el cluster responda.");
        }
    }
}
