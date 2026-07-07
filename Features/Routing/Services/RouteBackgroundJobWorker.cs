using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Routing.Services;

/// <summary>
/// Worker DB-backed que reclama trabajos de la cola de rutas y los despacha al
/// <see cref="RouteBackgroundJobProcessor"/>. Adaptado del subsistema de referencia.
/// </summary>
public sealed class RouteBackgroundJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RouteBackgroundJobWorker> log) : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResetStaleProcessingAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var job = await db.RouteBackgroundJobs
                    .Where(j => j.Status == RouteBackgroundJobStatuses.Pending)
                    .OrderBy(j => j.CreatedAtUtc)
                    .FirstOrDefaultAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (job is null)
                {
                    await Task.Delay(PollDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                job.Status = RouteBackgroundJobStatuses.Processing;
                job.StartedAtUtc = DateTimeOffset.UtcNow;
                job.AttemptCount++;
                await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                var processor = scope.ServiceProvider.GetRequiredService<RouteBackgroundJobProcessor>();
                try
                {
                    await processor.ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
                    job.Status = RouteBackgroundJobStatuses.Completed;
                    job.CompletedAtUtc = DateTimeOffset.UtcNow;
                    job.LastError = null;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Job de ruta {JobId} tipo {Type} falló", job.Id, job.JobType);
                    job.Status = RouteBackgroundJobStatuses.Failed;
                    job.CompletedAtUtc = DateTimeOffset.UtcNow;
                    job.LastError = ex.Message.Length > 3900 ? ex.Message[..3900] : ex.Message;
                }

                await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error en worker de cola de rutas");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ResetStaleProcessingAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTimeOffset.UtcNow - StaleProcessing;
            var stale = await db.RouteBackgroundJobs
                .Where(j => j.Status == RouteBackgroundJobStatuses.Processing
                            && j.StartedAtUtc != null
                            && j.StartedAtUtc < cutoff)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var j in stale)
            {
                j.Status = RouteBackgroundJobStatuses.Pending;
                j.StartedAtUtc = null;
            }

            if (stale.Count > 0)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                log.LogWarning("Se reencolaron {Count} jobs de ruta en Processing obsoletos.", stale.Count);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudieron reencolar jobs de ruta obsoletos al iniciar.");
        }
    }
}
