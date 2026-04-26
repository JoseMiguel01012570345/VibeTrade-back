using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.SavedOffers;

public sealed class SavedOffersService(AppDbContext db) : ISavedOffersService
{
    public async Task<IReadOnlyList<string>> GetFilteredForBootstrapAsync(
        string viewerUserId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == viewerUserId, cancellationToken);
        if (row is null)
            return Array.Empty<string>();

        var outList = new List<string>();
        foreach (var id in NormalizeIds(row.SavedOfferIds))
        {
            var owner = await GetOwnerUserIdForOfferIdAsync(id, cancellationToken);
            if (owner is null)
                continue;
            if (string.Equals(owner, viewerUserId, StringComparison.Ordinal))
                continue;
            outList.Add(id);
        }

        return outList;
    }

    public async Task<(SavedOfferMutationError Error, IReadOnlyList<string> SavedOfferIds)> TryAddAsync(
        string userId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        var pid = productId.Trim();
        if (string.IsNullOrEmpty(pid))
            return (SavedOfferMutationError.NotFound, Array.Empty<string>());

        var owner = await GetOwnerUserIdForOfferIdAsync(pid, cancellationToken);
        if (owner is null)
            return (SavedOfferMutationError.NotFound, Array.Empty<string>());

        if (string.Equals(owner, userId, StringComparison.Ordinal))
            return (SavedOfferMutationError.OwnProduct, Array.Empty<string>());

        var row = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
            return (SavedOfferMutationError.UserNotFound, Array.Empty<string>());

        var list = NormalizeIds(row.SavedOfferIds);
        if (!list.Contains(pid, StringComparer.Ordinal))
            list.Add(pid);

        row.SavedOfferIds = list;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return (SavedOfferMutationError.None, list);
    }

    public async Task<IReadOnlyList<string>?> TryRemoveAsync(
        string userId,
        string productId,
        CancellationToken cancellationToken = default)
    {
        var pid = productId.Trim();
        var row = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null)
            return null;

        var list = NormalizeIds(row.SavedOfferIds);
        var next = list.Where(x => !string.Equals(x, pid, StringComparison.Ordinal)).ToList();
        row.SavedOfferIds = next;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return next;
    }

    private async Task<string?> GetOwnerUserIdForOfferIdAsync(string offerId, CancellationToken cancellationToken)
    {
        // Publicaciones de hoja de ruta (`emo_*`): el "dueño" para guardados es quien publicó la ruta.
        if (offerId.StartsWith("emo_", StringComparison.Ordinal))
        {
            var publisher = await db.EmergentOffers.AsNoTracking()
                .Where(e => e.Id == offerId && e.RetractedAtUtc == null)
                .Select(e => e.PublisherUserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(publisher))
                return publisher;
            return null;
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

    private static List<string> NormalizeIds(IReadOnlyList<string>? ids)
    {
        if (ids is not { Count: > 0 })
            return new List<string>();
        return ids
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
