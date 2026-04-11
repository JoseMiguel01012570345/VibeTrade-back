using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogStoreDuplicateGuard
{
    public static void ThrowIfDuplicateNormalizedNames(AppDbContext db)
    {
        var tracked = db.ChangeTracker.Entries<StoreRow>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.NormalizedName is not null)
            .Select(e => e.Entity)
            .ToList();
        var dup = tracked
            .GroupBy(x => x.NormalizedName!, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new DuplicateStoreNameException(dup.Key);
    }
}
