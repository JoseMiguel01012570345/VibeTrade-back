using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Market.Offers;

public sealed class OfferEngagementService(
    AppDbContext db,
    IOfferPopularityWeightService popularityWeight) : IOfferEngagementService
{
    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..16];

    public async Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return false;
        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(oid))
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

    public async Task EnrichHomeOffersAsync(
        Dictionary<string, HomeOfferViewDto> offers,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        if (offers.Count == 0)
            return;

        var ids = offers.Keys.ToList();
        var likeCounts = await db.OfferLikes.AsNoTracking()
            .Where(x => ids.Contains(x.OfferId))
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerOfferIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            var liked = await db.OfferLikes.AsNoTracking()
                .Where(x => ids.Contains(x.OfferId) && x.LikerKey == likerKey)
                .Select(x => x.OfferId)
                .ToListAsync(cancellationToken);
            viewerOfferIds = liked.ToHashSet(StringComparer.Ordinal);
        }

        foreach (var kv in offers)
        {
            var oid = kv.Key;
            var obj = kv.Value;
            var n = obj.Qa?.Count ?? 0;
            obj.PublicCommentCount = n;
            obj.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            obj.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task EnrichStoreCatalogBlockEngagementAsync(
        IReadOnlyList<StoreProductCatalogRowView> products,
        IReadOnlyList<StoreServiceCatalogRowView> services,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>(products.Count + services.Count);
        foreach (var p in products)
        {
            var oid = p.Id?.Trim() ?? "";
            if (oid.Length >= 2)
                ids.Add(oid);
        }

        foreach (var s in services)
        {
            var oid = s.Id?.Trim() ?? "";
            if (oid.Length >= 2)
                ids.Add(oid);
        }

        if (ids.Count == 0)
            return;

        var likeCounts = await db.OfferLikes.AsNoTracking()
            .Where(x => ids.Contains(x.OfferId))
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerOfferIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            var liked = await db.OfferLikes.AsNoTracking()
                .Where(x => ids.Contains(x.OfferId) && x.LikerKey == likerKey)
                .Select(x => x.OfferId)
                .ToListAsync(cancellationToken);
            viewerOfferIds = liked.ToHashSet(StringComparer.Ordinal);
        }

        foreach (var p in products)
        {
            var oid = p.Id?.Trim() ?? "";
            if (oid.Length < 2)
                continue;
            p.PublicCommentCount = p.Qa?.Count ?? 0;
            p.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            p.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }

        foreach (var s in services)
        {
            var oid = s.Id?.Trim() ?? "";
            if (oid.Length < 2)
                continue;
            s.PublicCommentCount = s.Qa?.Count ?? 0;
            s.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            s.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task<IReadOnlyList<OfferQaItemResponseDto>> EnrichOfferQaListAsync(
        string offerId,
        IReadOnlyList<OfferQaComment> qa,
        string? likerKey,
        CancellationToken cancellationToken = default)
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

    public async Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(
        string offerId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return (false, 0);

        if (!await OfferExistsAsync(oid, cancellationToken))
            return (false, 0);

        var existing = await db.OfferLikes
            .FirstOrDefaultAsync(x => x.OfferId == oid && x.LikerKey == likerKey, cancellationToken);

        if (existing is not null)
        {
            db.OfferLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await popularityWeight.RecomputeAsync(oid, cancellationToken);
            var c = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
            return (false, c);
        }

        db.OfferLikes.Add(new OfferLikeRow
        {
            Id = NewId("olk_"),
            OfferId = oid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await popularityWeight.RecomputeAsync(oid, cancellationToken);
        var c2 = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
        return (true, c2);
    }

    public async Task<(bool Liked, int LikeCount)> ToggleQaCommentLikeAsync(
        string offerId,
        string qaCommentId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        var cid = (qaCommentId ?? "").Trim();
        if (oid.Length < 2 || cid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return (false, 0);

        if (!await OfferExistsAsync(oid, cancellationToken))
            return (false, 0);

        var existing = await db.OfferQaCommentLikes
            .FirstOrDefaultAsync(
                x => x.OfferId == oid && x.QaCommentId == cid && x.LikerKey == likerKey,
                cancellationToken);

        if (existing is not null)
        {
            db.OfferQaCommentLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await popularityWeight.RecomputeAsync(oid, cancellationToken);
            var c = await db.OfferQaCommentLikes.CountAsync(
                x => x.OfferId == oid && x.QaCommentId == cid,
                cancellationToken);
            return (false, c);
        }

        db.OfferQaCommentLikes.Add(new OfferQaCommentLikeRow
        {
            Id = NewId("oqk_"),
            OfferId = oid,
            QaCommentId = cid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await popularityWeight.RecomputeAsync(oid, cancellationToken);
        var c2 = await db.OfferQaCommentLikes.CountAsync(
            x => x.OfferId == oid && x.QaCommentId == cid,
            cancellationToken);
        return (true, c2);
    }
}
