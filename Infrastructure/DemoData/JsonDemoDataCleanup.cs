using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Infrastructure.DemoData;

/// <summary>
/// Removes rows keyed by <c>demo-seed.json</c> (and the matching <c>generate_demo_seed.py</c> id scheme).
/// When the JSON file is missing, falls back to users whose id matches <c>cuba_demo_u%</c>.
/// </summary>
internal static class JsonDemoDataCleanup
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private const string CubaDemoUserPattern = "cuba_demo_u%";
    private const string CubaDemoContactPattern = "cuba_demo_uc_%";

    public static async Task RunWhenDisabledAsync(
        AppDbContext db,
        ILogger logger,
        string absoluteDataFilePath,
        CancellationToken cancellationToken = default)
    {
        DemoSeedDocument? doc = null;
        if (File.Exists(absoluteDataFilePath))
        {
            await using var stream = File.OpenRead(absoluteDataFilePath);
            doc = await JsonSerializer.DeserializeAsync<DemoSeedDocument>(stream, JsonReadOptions, cancellationToken);
        }
        else
            logger.LogWarning("Demo cleanup: seed file not found ({Path}); using DB fallback for cuba_demo_* ids.", absoluteDataFilePath);

        var scope = await ResolveRemovalScopeAsync(db, doc, cancellationToken);
        if (scope.UserIds.Count == 0)
        {
            logger.LogInformation("Demo cleanup: no JSON-backed demo users in scope; nothing to remove.");
            return;
        }

        await RemoveScopedDemoDataAsync(db, scope, cancellationToken);
        logger.LogInformation(
            "Demo cleanup: removed demo dataset ({UserCount} users, {StoreCount} stores, {OfferCount} offers).",
            scope.UserIds.Count,
            scope.StoreIds.Count,
            scope.OfferIds.Count);
    }

    private sealed class RemovalScope
    {
        public HashSet<string> UserIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> StoreIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OfferIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PhoneDigits { get; } = new(StringComparer.Ordinal);
    }

    private static async Task<RemovalScope> ResolveRemovalScopeAsync(
        AppDbContext db,
        DemoSeedDocument? doc,
        CancellationToken cancellationToken)
    {
        var scope = new RemovalScope();

        if (doc?.Users is { Count: > 0 })
        {
            foreach (var u in doc.Users)
            {
                var uid = (u.Id ?? "").Trim();
                if (uid.Length == 0)
                    continue;
                scope.UserIds.Add(uid);
                var pd = (u.PhoneDigits ?? "").Trim();
                if (pd.Length > 0)
                    scope.PhoneDigits.Add(pd);
                foreach (var store in u.Stores)
                {
                    var sid = (store.Id ?? "").Trim();
                    if (sid.Length > 0)
                        scope.StoreIds.Add(sid);
                    foreach (var p in store.Products)
                    {
                        var pid = (p.Id ?? "").Trim();
                        if (pid.Length > 0)
                            scope.OfferIds.Add(pid);
                    }

                    foreach (var s in store.Services)
                    {
                        var svid = (s.Id ?? "").Trim();
                        if (svid.Length > 0)
                            scope.OfferIds.Add(svid);
                    }
                }
            }

            return scope;
        }

        var fallbackUserIds = await db.UserAccounts.AsNoTracking()
            .Where(u => EF.Functions.Like(u.Id, CubaDemoUserPattern))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in fallbackUserIds)
            scope.UserIds.Add(id);

        if (scope.UserIds.Count == 0)
            return scope;

        var storeRows = await db.Stores.AsNoTracking()
            .Where(s => scope.UserIds.Contains(s.OwnerUserId))
            .Select(s => new { s.Id })
            .ToListAsync(cancellationToken);
        foreach (var s in storeRows)
            scope.StoreIds.Add(s.Id);

        if (scope.StoreIds.Count == 0)
        {
            var phones = await db.UserAccounts.AsNoTracking()
                .Where(u => scope.UserIds.Contains(u.Id))
                .Select(u => u.PhoneDigits)
                .ToListAsync(cancellationToken);
            foreach (var p in phones)
            {
                var d = (p ?? "").Trim();
                if (d.Length > 0)
                    scope.PhoneDigits.Add(d);
            }

            return scope;
        }

        var offerIdLists = await Task.WhenAll(
            db.StoreProducts.IgnoreQueryFilters().AsNoTracking()
                .Where(p => scope.StoreIds.Contains(p.StoreId))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken),
            db.StoreServices.IgnoreQueryFilters().AsNoTracking()
                .Where(s => scope.StoreIds.Contains(s.StoreId))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken));
        foreach (var oid in offerIdLists[0])
            scope.OfferIds.Add(oid);
        foreach (var oid in offerIdLists[1])
            scope.OfferIds.Add(oid);

        var phoneRows = await db.UserAccounts.AsNoTracking()
            .Where(u => scope.UserIds.Contains(u.Id))
            .Select(u => u.PhoneDigits)
            .ToListAsync(cancellationToken);
        foreach (var p in phoneRows)
        {
            var d = (p ?? "").Trim();
            if (d.Length > 0)
                scope.PhoneDigits.Add(d);
        }

        return scope;
    }

    private static async Task RemoveScopedDemoDataAsync(AppDbContext db, RemovalScope scope, CancellationToken cancellationToken)
    {
        var userSet = scope.UserIds;
        var storeSet = scope.StoreIds;
        var offerSet = scope.OfferIds;

        var threadIds = await db.ChatThreads.AsNoTracking()
            .Where(t =>
                (t.OfferId != null && offerSet.Contains(t.OfferId))
                || storeSet.Contains(t.StoreId)
                || userSet.Contains(t.BuyerUserId)
                || userSet.Contains(t.SellerUserId)
                || (t.InitiatorUserId != null && userSet.Contains(t.InitiatorUserId)))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var threadSet = threadIds.Count > 0
            ? new HashSet<string>(threadIds, StringComparer.Ordinal)
            : null;

        if (threadSet is { Count: > 0 })
        {
            await db.EmergentOffers
                .Where(e => threadSet.Contains(e.ThreadId) || offerSet.Contains(e.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.EmergentOffers
                .Where(e => offerSet.Contains(e.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (offerSet.Count > 0)
        {
            await db.OfferQaCommentLikes
                .Where(x => offerSet.Contains(x.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.OfferLikes
                .Where(x => offerSet.Contains(x.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.TrustScoreLedgerRows
            .Where(x =>
                userSet.Contains(x.SubjectId)
                || storeSet.Contains(x.SubjectId)
                || (offerSet.Count > 0 && offerSet.Contains(x.SubjectId)))
            .ExecuteDeleteAsync(cancellationToken);

        await db.UserOfferInteractions
            .Where(x =>
                userSet.Contains(x.UserId)
                || (offerSet.Count > 0 && offerSet.Contains(x.OfferId)))
            .ExecuteDeleteAsync(cancellationToken);

        if (threadSet is { Count: > 0 })
        {
            await db.ChatNotifications
                .Where(n => n.ThreadId != null && threadSet.Contains(n.ThreadId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (offerSet.Count > 0)
        {
            await db.ChatNotifications
                .Where(n => n.OfferId != null && offerSet.Contains(n.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.ChatNotifications
            .Where(n =>
                userSet.Contains(n.RecipientUserId)
                || (n.SenderUserId != null && userSet.Contains(n.SenderUserId)))
            .ExecuteDeleteAsync(cancellationToken);

        if (threadSet is { Count: > 0 })
            await db.ChatThreads.Where(t => threadSet.Contains(t.Id)).ExecuteDeleteAsync(cancellationToken);

        if (storeSet.Count > 0)
            await db.Stores.Where(s => storeSet.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);

        await db.UserContacts.IgnoreQueryFilters()
            .Where(c =>
                EF.Functions.Like(c.Id, CubaDemoContactPattern)
                || userSet.Contains(c.OwnerUserId)
                || userSet.Contains(c.ContactUserId))
            .ExecuteDeleteAsync(cancellationToken);

        await DeleteAuthSessionsForUsersAsync(db, userSet, cancellationToken);

        if (scope.PhoneDigits.Count > 0)
        {
            await db.AuthPendingOtps
                .Where(o => scope.PhoneDigits.Contains(o.PhoneDigits))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.UserAccounts.Where(u => userSet.Contains(u.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task DeleteAuthSessionsForUsersAsync(
        AppDbContext db,
        HashSet<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return;
        var arr = userIds.ToArray();
        await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM auth_sessions WHERE ("UserJson"->>'id') = ANY (@ids)""",
            new object[] { new NpgsqlParameter("ids", arr) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text } },
            cancellationToken);
    }
}
