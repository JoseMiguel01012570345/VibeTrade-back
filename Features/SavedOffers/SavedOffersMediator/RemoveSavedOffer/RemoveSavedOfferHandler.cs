using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.SavedOffers.Shared;

namespace VibeTrade.Backend.Features.SavedOffers.SavedOffersMediator.RemoveSavedOffer;

public sealed class RemoveSavedOfferHandler(AppDbContext db)
    : IRequestHandler<RemoveSavedOfferCommand, IReadOnlyList<string>?>
{
    public async Task<IReadOnlyList<string>?> Handle(RemoveSavedOfferCommand request, CancellationToken cancellationToken)
    {
        var pid = request.ProductId.Trim();
        var row = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (row is null)
            return null;

        var list = SavedOfferOwnerLookup.NormalizeIds(row.SavedOfferIds);
        var next = list.Where(x => !string.Equals(x, pid, StringComparison.Ordinal)).ToList();
        row.SavedOfferIds = next;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return next;
    }
}
