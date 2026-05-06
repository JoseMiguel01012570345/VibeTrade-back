using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VibeTrade.Backend.Features.Recommendations.Popularity;

public sealed class OfferPopularityWeightBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OfferPopularityWeightOptions> options,
    ILogger<OfferPopularityWeightBackfillHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackfillOnStartup)
            return;

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IOfferPopularityWeightService>();
            await svc.RecomputeAllPublishedAsync(stoppingToken);
            logger.LogInformation("Popularity weight backfill completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Popularity weight backfill failed.");
        }
    }
}
