using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.SavedOffers.Shared;

namespace VibeTrade.Backend.Features.SavedOffers.GetFilteredSavedOffers;

public sealed class GetFilteredSavedOffersHandler(AppDbContext db)
    : IRequestHandler<GetFilteredSavedOffersQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetFilteredSavedOffersQuery request, CancellationToken cancellationToken)
    {
        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.ViewerUserId, cancellationToken);
        if (row is null)
            return Array.Empty<string>();

        var outList = new List<string>();
        foreach (var id in SavedOfferOwnerLookup.NormalizeIds(row.SavedOfferIds))
        {
            var owner = await SavedOfferOwnerLookup.GetOwnerUserIdForOfferIdAsync(db, id, cancellationToken);
            if (owner is null)
                continue;
            if (string.Equals(owner, request.ViewerUserId, StringComparison.Ordinal))
                continue;
            outList.Add(id);
        }

        return outList;
    }
}
