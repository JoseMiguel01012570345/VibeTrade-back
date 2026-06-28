using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.SavedOffers.Shared;

internal static class SavedOfferOwnerLookup
{
    internal static List<string> NormalizeIds(IReadOnlyList<string>? ids)
    {
        if (ids is not { Count: > 0 })
            return new List<string>();
        return ids
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal static async Task<string?> GetOwnerUserIdForOfferIdAsync(
        AppDbContext db,
        string offerId,
        CancellationToken cancellationToken)
    {
        if (offerId.StartsWith("emo_", StringComparison.Ordinal))
        {
            var publisher = await db.EmergentOffers.AsNoTracking()
                .Where(e => e.Id == offerId && e.RetractedAtUtc == null)
                .Select(e => e.PublisherUserId)
                .FirstOrDefaultAsync(cancellationToken);
            return string.IsNullOrEmpty(publisher) ? null : publisher;
        }

        var storeFromProduct = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Id == offerId)
            .Select(p => p.StoreId)
            .FirstOrDefaultAsync(cancellationToken);
        if (storeFromProduct is not null)
        {
            return await db.Stores.AsNoTracking()
                .Where(s => s.Id == storeFromProduct)
                .Select(s => s.OwnerUserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var storeFromService = await db.StoreServices.AsNoTracking()
            .Where(s => s.Id == offerId)
            .Select(s => s.StoreId)
            .FirstOrDefaultAsync(cancellationToken);
        if (storeFromService is null)
            return null;

        return await db.Stores.AsNoTracking()
            .Where(s => s.Id == storeFromService)
            .Select(s => s.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
