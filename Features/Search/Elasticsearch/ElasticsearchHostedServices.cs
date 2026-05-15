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

/// <summary>
/// Reindexa el catálogo en Elasticsearch una vez al día a las 00:00 UTC (12:00 AM UTC, convención del spec de producto).
/// </summary>
public sealed class ElasticsearchDailyReindexHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ElasticsearchStoreSearchOptions> options,
    ILogger<ElasticsearchDailyReindexHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Uri))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextMidnightUtc();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var writer = scope.ServiceProvider.GetRequiredService<IStoreSearchIndexWriter>();
                await writer.ReindexAllStoresAsync(stoppingToken);
                logger.LogInformation("Elasticsearch: reindex diario (00:00 UTC) completado.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Elasticsearch: reindex diario falló.");
            }
        }
    }

    private static TimeSpan DelayUntilNextMidnightUtc()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMidnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var d = nextMidnight - now;
        return d <= TimeSpan.Zero ? TimeSpan.FromHours(24) : d;
    }
}
