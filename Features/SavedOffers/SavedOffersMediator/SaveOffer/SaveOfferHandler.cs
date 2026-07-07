using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.SavedOffers.Interfaces;
using VibeTrade.Backend.Features.SavedOffers.Shared;

namespace VibeTrade.Backend.Features.SavedOffers.SavedOffersMediator.SaveOffer;

public sealed class SaveOfferHandler(AppDbContext db) : IRequestHandler<SaveOfferCommand, SaveOfferResult>
{
    public async Task<SaveOfferResult> Handle(SaveOfferCommand request, CancellationToken cancellationToken)
    {
        var pid = request.ProductId.Trim();
        if (string.IsNullOrEmpty(pid))
            return new SaveOfferResult(SavedOfferMutationError.NotFound, Array.Empty<string>());

        var owner = await SavedOfferOwnerLookup.GetOwnerUserIdForOfferIdAsync(db, pid, cancellationToken);
        if (owner is null)
            return new SaveOfferResult(SavedOfferMutationError.NotFound, Array.Empty<string>());

        if (string.Equals(owner, request.UserId, StringComparison.Ordinal))
            return new SaveOfferResult(SavedOfferMutationError.OwnProduct, Array.Empty<string>());

        var row = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (row is null)
            return new SaveOfferResult(SavedOfferMutationError.UserNotFound, Array.Empty<string>());

        var list = SavedOfferOwnerLookup.NormalizeIds(row.SavedOfferIds);
        if (!list.Contains(pid, StringComparer.Ordinal))
            list.Add(pid);

        row.SavedOfferIds = list;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new SaveOfferResult(SavedOfferMutationError.None, list);
    }
}
