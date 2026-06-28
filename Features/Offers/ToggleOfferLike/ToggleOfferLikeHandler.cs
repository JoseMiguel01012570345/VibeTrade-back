using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Offers.ToggleOfferLike;

public sealed class ToggleOfferLikeHandler(
    AppDbContext db,
    IOfferPopularityWeightService popularityWeight) : IRequestHandler<ToggleOfferLikeCommand, ToggleOfferLikeResult>
{
    public async Task<ToggleOfferLikeResult> Handle(
        ToggleOfferLikeCommand request,
        CancellationToken cancellationToken)
    {
        var oid = (request.OfferId ?? "").Trim();
        var likerKey = request.LikerKey ?? "";
        if (oid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return new ToggleOfferLikeResult(false, 0, false);

        if (!await OfferExistsAsync(oid, cancellationToken))
            return new ToggleOfferLikeResult(false, 0, false);

        var existing = await db.OfferLikes
            .FirstOrDefaultAsync(x => x.OfferId == oid && x.LikerKey == likerKey, cancellationToken);

        if (existing is not null)
        {
            db.OfferLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await popularityWeight.RecomputeAsync(oid, cancellationToken);
            var c = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
            return new ToggleOfferLikeResult(false, c, true);
        }

        db.OfferLikes.Add(new OfferLikeRow
        {
            Id = OfferUtils.NewId("olk_"),
            OfferId = oid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await popularityWeight.RecomputeAsync(oid, cancellationToken);
        var c2 = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
        return new ToggleOfferLikeResult(true, c2, true);
    }

    private async Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return false;
        if (OfferUtils.IsEmergentPublicationId(oid))
        {
            return await db.EmergentOffers.AsNoTracking()
                .AnyAsync(e =>
                    e.Id == oid
                    && e.RetractedAtUtc == null
                    && db.ChatRouteSheets.Any(r =>
                        r.ThreadId == e.ThreadId
                        && r.RouteSheetId == e.RouteSheetId
                        && r.DeletedAtUtc == null
                        && r.PublishedToPlatform),
                    cancellationToken);
        }
        if (await db.StoreProducts.AsNoTracking().AnyAsync(p => p.Id == oid, cancellationToken))
            return true;
        return await db.StoreServices.AsNoTracking().AnyAsync(s => s.Id == oid, cancellationToken);
    }
}
