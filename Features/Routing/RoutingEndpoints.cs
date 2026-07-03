using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Routing;

public static class RoutingEndpoints
{
    public static WebApplication MapRoutingEndpoints(this WebApplication app)
    {
        const string tag = "Route calculation";

        app.MapPost(
                "/api/v1/chat/threads/{threadId}/route-sheets/{routeSheetId}/route/calculate",
                CalculateRouteAsync)
            .WithTags(tag);
        app.MapGet(
                "/api/v1/chat/threads/{threadId}/route-sheets/{routeSheetId}/route/status",
                GetRouteStatusAsync)
            .WithTags(tag);

        return app;
    }

    private static async Task<IResult> CalculateRouteAsync(
        string threadId,
        string routeSheetId,
        HttpRequest request,
        AppDbContext db,
        IChatService chat,
        IRouteBackgroundJobEnqueueService enqueue,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return Results.NotFound();
        if (!await chat.UserCanAccessThreadRowAsync(userId.Trim(), thread, cancellationToken).ConfigureAwait(false))
            return Results.Json(new { message = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);

        var exists = await db.ChatRouteSheets.AsNoTracking()
            .AnyAsync(r => r.ThreadId == tid && r.RouteSheetId == rsid && r.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            return Results.NotFound();

        var jobId = await enqueue.EnqueueRouteSheetCalculationAsync(tid, rsid, cancellationToken).ConfigureAwait(false);
        return Results.Accepted(value: new { jobId, status = RouteCalculationStatuses.Pending });
    }

    private static async Task<IResult> GetRouteStatusAsync(
        string threadId,
        string routeSheetId,
        HttpRequest request,
        AppDbContext db,
        IChatService chat,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return Results.NotFound();
        if (!await chat.UserCanAccessThreadRowAsync(userId.Trim(), thread, cancellationToken).ConfigureAwait(false))
            return Results.Json(new { message = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);

        var calc = await db.RouteSheetRouteCalculations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ThreadId == tid && c.RouteSheetId == rsid, cancellationToken)
            .ConfigureAwait(false);
        if (calc is null)
            return Results.Ok(new { status = RouteCalculationStatuses.None });

        return Results.Ok(new
        {
            status = calc.Status,
            totalKm = calc.TotalKm,
            visitOrderJson = calc.VisitOrderJson,
            lastError = calc.LastError,
            updatedAtUtc = calc.UpdatedAtUtc,
        });
    }
}
