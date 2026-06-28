using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Trust;

public static class TrustModule
{
    private const int MinTrust = -10_000;
    private const int MaxAbsDeltaPerRequest = 10_000;

    public static IServiceCollection AddTrustFeature(this IServiceCollection services)
    {
        services.AddScoped<ITrustScoreLedgerService, TrustScoreLedgerService>();
        services.AddScoped<AgreementCompletionTrustService>();
        return services;
    }

    public static WebApplication MapTrustLedgerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Trust");

        group.MapGet("/me/trust-history", GetMyHistoryAsync);
        group.MapPost("/me/trust-adjust", PostMyAdjustAsync);
        group.MapGet("/stores/{storeId}/trust-history", GetStoreHistoryAsync);
        group.MapPost("/stores/{storeId}/trust-adjust", PostStoreAdjustAsync);

        return app;
    }

    private static int ApplyDelta(int current, int delta) => Math.Max(MinTrust, current + delta);

    private static async Task<IResult> GetMyHistoryAsync(
        int limit,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        ITrustScoreLedgerService ledger,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await ledger.ListForSubjectAsync(
            TrustLedgerSubjects.User,
            userId,
            limit,
            cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostMyAdjustAsync(
        TrustAdjustRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        AppDbContext db,
        ITrustScoreLedgerService ledger,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body.Delta == 0)
            return Results.BadRequest(new { error = "invalid_delta", message = "Delta no puede ser 0." });
        if (Math.Abs(body.Delta) > MaxAbsDeltaPerRequest)
            return Results.BadRequest(new { error = "delta_too_large", message = "Delta fuera del rango permitido (demo)." });

        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (acc is null)
            return Results.NotFound(new { error = "user_not_found", message = "No se encontró la cuenta." });

        var prev = acc.TrustScore;
        var next = ApplyDelta(prev, body.Delta);
        var appliedDelta = next - prev;
        if (appliedDelta == 0)
            return Results.BadRequest(new { error = "no_change", message = "El ajuste no modifica el puntaje (límite alcanzado)." });

        acc.TrustScore = next;
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "Ajuste de confianza (demo)" : body.Reason.Trim();
        ledger.StageEntry(TrustLedgerSubjects.User, userId, appliedDelta, next, reason);
        await db.SaveChangesAsync(cancellationToken);

        var entryRow = await db.TrustScoreLedgerRows.AsNoTracking()
            .Where(x => x.SubjectType == TrustLedgerSubjects.User && x.SubjectId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);
        var entry = new TrustHistoryItemDto(
            entryRow.Id,
            entryRow.CreatedAtUtc,
            entryRow.Delta,
            entryRow.BalanceAfter,
            entryRow.Reason);
        return Results.Ok(new TrustAdjustResponse(next, entry));
    }

    private static async Task<IResult> GetStoreHistoryAsync(
        string storeId,
        int limit,
        AppDbContext db,
        ITrustScoreLedgerService ledger,
        CancellationToken cancellationToken)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return Results.NotFound();
        var exists = await db.Stores.AsNoTracking().AnyAsync(x => x.Id == sid, cancellationToken);
        if (!exists)
            return Results.NotFound(new { error = "store_not_found", message = "No se encontró la tienda." });
        var list = await ledger.ListForSubjectAsync(TrustLedgerSubjects.Store, sid, limit, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostStoreAdjustAsync(
        string storeId,
        TrustAdjustRequest body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        AppDbContext db,
        ITrustScoreLedgerService ledger,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body.Delta == 0)
            return Results.BadRequest(new { error = "invalid_delta", message = "Delta no puede ser 0." });
        if (Math.Abs(body.Delta) > MaxAbsDeltaPerRequest)
            return Results.BadRequest(new { error = "delta_too_large", message = "Delta fuera del rango permitido (demo)." });

        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return Results.NotFound();
        var store = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (store is null)
            return Results.NotFound(new { error = "store_not_found", message = "No se encontró la tienda." });
        if (!string.Equals((store.OwnerUserId ?? "").Trim(), userId, StringComparison.Ordinal))
            return Results.Json(new { error = "forbidden", message = "Solo el dueño puede ajustar la confianza de la tienda." }, statusCode: StatusCodes.Status403Forbidden);

        var prev = store.TrustScore;
        var next = ApplyDelta(prev, body.Delta);
        var appliedDelta = next - prev;
        if (appliedDelta == 0)
            return Results.BadRequest(new { error = "no_change", message = "El ajuste no modifica el puntaje (límite alcanzado)." });

        store.TrustScore = next;
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "Ajuste a la tienda (demo)" : body.Reason.Trim();
        ledger.StageEntry(TrustLedgerSubjects.Store, sid, appliedDelta, next, reason);
        await db.SaveChangesAsync(cancellationToken);

        var entryRow = await db.TrustScoreLedgerRows.AsNoTracking()
            .Where(x => x.SubjectType == TrustLedgerSubjects.Store && x.SubjectId == sid)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);
        var entry = new TrustHistoryItemDto(
            entryRow.Id,
            entryRow.CreatedAtUtc,
            entryRow.Delta,
            entryRow.BalanceAfter,
            entryRow.Reason);
        return Results.Ok(new TrustAdjustResponse(next, entry));
    }
}
