using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Infrastructure.DemoData;

/// <summary>
/// Removes rows created by the old procedural demo seed (<c>demo_seed_*</c> ids) so the JSON-based
/// dataset can replace them without duplicate keys or stale offers.
/// </summary>
internal static class LegacyDemoDataCleanup
{
    private const string LegacyUserPattern = "demo_seed_user_%";
    private const string LegacyStorePattern = "demo_seed_store_%";
    private const string LegacyContactPattern = "demo_uc_%";

    public static async Task RunAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var legacyUserIds = await db.UserAccounts.AsNoTracking()
            .Where(u => EF.Functions.Like(u.Id, LegacyUserPattern))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (legacyUserIds.Count == 0)
            return;

        var legacyStoreIds = await db.Stores.AsNoTracking()
            .Where(s => EF.Functions.Like(s.Id, LegacyStorePattern))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var legacyOfferIds = new List<string>(capacity: 512);
        if (legacyStoreIds.Count > 0)
        {
            legacyOfferIds.AddRange(await db.StoreProducts.AsNoTracking()
                .Where(p => legacyStoreIds.Contains(p.StoreId))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken));
            legacyOfferIds.AddRange(await db.StoreServices.AsNoTracking()
                .Where(s => legacyStoreIds.Contains(s.StoreId))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken));
        }

        var legacyOfferSet = legacyOfferIds.Count > 0
            ? new HashSet<string>(legacyOfferIds, StringComparer.Ordinal)
            : null;

        if (legacyOfferSet is { Count: > 0 })
        {
            await db.OfferQaCommentLikes
                .Where(x => legacyOfferSet.Contains(x.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.OfferLikes
                .Where(x => legacyOfferSet.Contains(x.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.UserOfferInteractions
            .Where(x =>
                legacyUserIds.Contains(x.UserId)
                || (legacyOfferSet != null && legacyOfferSet.Contains(x.OfferId)))
            .ExecuteDeleteAsync(cancellationToken);

        var threadIds = await db.ChatThreads.AsNoTracking()
            .Where(t =>
                (legacyOfferSet != null && legacyOfferSet.Contains(t.OfferId))
                || legacyStoreIds.Contains(t.StoreId)
                || legacyUserIds.Contains(t.BuyerUserId)
                || legacyUserIds.Contains(t.SellerUserId))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (threadIds.Count > 0)
        {
            var threadSet = new HashSet<string>(threadIds, StringComparer.Ordinal);
            await db.ChatNotifications
                .Where(n => n.ThreadId != null && threadSet.Contains(n.ThreadId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.ChatMessages
                .Where(m => threadSet.Contains(m.ThreadId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.ChatThreads
                .Where(t => threadSet.Contains(t.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (legacyOfferSet is { Count: > 0 })
        {
            await db.ChatNotifications
                .Where(n => n.OfferId != null && legacyOfferSet.Contains(n.OfferId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (legacyStoreIds.Count > 0)
        {
            var storeSet = new HashSet<string>(legacyStoreIds, StringComparer.Ordinal);
            await db.Stores.Where(s => storeSet.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        await db.UserContacts
            .Where(c =>
                EF.Functions.Like(c.Id, LegacyContactPattern)
                || legacyUserIds.Contains(c.OwnerUserId)
                || legacyUserIds.Contains(c.ContactUserId))
            .ExecuteDeleteAsync(cancellationToken);

        await db.UserAccounts
            .Where(u => legacyUserIds.Contains(u.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
