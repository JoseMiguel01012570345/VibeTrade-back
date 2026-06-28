using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Catalog.Interfaces;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Offers.GetOfferQa;

public sealed class GetOfferQaHandler(
    IMarketCatalogSyncService catalog,
    AppDbContext db) : IRequestHandler<GetOfferQaQuery, GetOfferQaResult>
{
    public async Task<GetOfferQaResult> Handle(GetOfferQaQuery request, CancellationToken cancellationToken)
    {
        var qa = await catalog.GetOfferQaForOfferAsync(request.OfferId, cancellationToken);
        if (qa is null)
            return new GetOfferQaResult(null, false);

        var enriched = await EnrichAsync(request.OfferId, qa, request.LikerKey, cancellationToken);
        return new GetOfferQaResult(enriched, true);
    }

    private async Task<IReadOnlyList<OfferQaItemResponseDto>> EnrichAsync(
        string offerId,
        IReadOnlyList<OfferQaComment> qa,
        string? likerKey,
        CancellationToken cancellationToken)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Array.Empty<OfferQaItemResponseDto>();

        var list = new List<OfferQaItemResponseDto>(qa.Count);
        foreach (var c in qa)
        {
            list.Add(new OfferQaItemResponseDto
            {
                Id = c.Id,
                Text = c.Text,
                Question = c.Question,
                ParentId = c.ParentId,
                AskedBy = c.AskedBy,
                Author = c.Author,
                CreatedAt = c.CreatedAt,
                Answer = c.Answer,
            });
        }

        if (list.Count == 0)
            return list;

        var commentIds = list.Select(x => x.Id).Where(id => id.Length > 0).ToList();
        if (commentIds.Count == 0)
            return list;

        var likeCounts = await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => x.OfferId == oid && commentIds.Contains(x.QaCommentId))
            .GroupBy(x => x.QaCommentId)
            .Select(g => new { QaCommentId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.QaCommentId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerLikedIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            viewerLikedIds = (await db.OfferQaCommentLikes.AsNoTracking()
                .Where(x => x.OfferId == oid && commentIds.Contains(x.QaCommentId) && x.LikerKey == likerKey)
                .Select(x => x.QaCommentId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        }

        foreach (var o in list)
        {
            var cid = o.Id;
            o.LikeCount = likeCounts.GetValueOrDefault(cid, 0);
            o.ViewerLiked = viewerLikedIds is not null && viewerLikedIds.Contains(cid);
        }

        return list;
    }
}
