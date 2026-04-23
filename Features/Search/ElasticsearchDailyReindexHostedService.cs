using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Search;

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
