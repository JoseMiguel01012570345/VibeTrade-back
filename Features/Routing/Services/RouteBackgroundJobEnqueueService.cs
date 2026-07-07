using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Routing.Entities;
using VibeTrade.Backend.Features.Routing.Interfaces;

namespace VibeTrade.Backend.Features.Routing.Services;

public sealed class RouteBackgroundJobEnqueueService(AppDbContext db) : IRouteBackgroundJobEnqueueService
{
    public Task<string> EnqueueRouteSheetCalculationAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(RouteBackgroundJobTypes.RouteSheetRouteCalculation, threadId, routeSheetId, cancellationToken);

    public Task<string> EnqueueRouteSheetMatrixRebuildAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(RouteBackgroundJobTypes.RouteSheetMatrixRebuild, threadId, routeSheetId, cancellationToken);

    private async Task<string> EnqueueAsync(
        string jobType,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var now = DateTimeOffset.UtcNow;

        var job = new RouteBackgroundJobRow
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = jobType,
            Status = RouteBackgroundJobStatuses.Pending,
            ThreadId = tid,
            RouteSheetId = rsid,
            CreatedAtUtc = now,
        };
        db.RouteBackgroundJobs.Add(job);

        var calc = await db.RouteSheetRouteCalculations
            .FirstOrDefaultAsync(c => c.ThreadId == tid && c.RouteSheetId == rsid, cancellationToken)
            .ConfigureAwait(false);
        if (calc is null)
        {
            calc = new RouteSheetRouteCalculationRow
            {
                Id = Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                RouteSheetId = rsid,
                UpdatedAtUtc = now,
            };
            db.RouteSheetRouteCalculations.Add(calc);
        }
        calc.Status = RouteCalculationStatuses.Pending;
        calc.LastError = null;
        calc.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return job.Id;
    }
}
