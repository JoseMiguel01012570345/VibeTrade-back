using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Market;

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
        if (await db.StoreProducts.AsNoTracking().AnyAsync(p => p.Id == oid, cancellationToken))
            return true;
        return await db.StoreServices.AsNoTracking().AnyAsync(s => s.Id == oid, cancellationToken);
    }

    public async Task EnrichOffersJsonAsync(JsonObject offers, string? likerKey, CancellationToken cancellationToken = default)
    {
        if (offers.Count == 0)
            return;

        var ids = offers.Select(kv => kv.Key).ToList();
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
            if (kv.Value is not JsonObject obj)
                continue;
            var oid = kv.Key;
            var n = 0;
            if (obj.TryGetPropertyValue("qa", out var qaNode) && qaNode is JsonArray qaArr)
                n = qaArr.Count;
            obj["publicCommentCount"] = n;
            obj["offerLikeCount"] = likeCounts.GetValueOrDefault(oid, 0);
            obj["viewerLikedOffer"] = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task<string?> EnrichOfferQaJsonAsync(
        string offerId,
        IReadOnlyList<OfferQaComment> qa,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return null;

        JsonArray? arr;
        try
        {
            var root = JsonNode.Parse(OfferQaJson.ToJsonb(qa.ToList()));
            arr = root as JsonArray;
        }
        catch
        {
            arr = new JsonArray();
        }

        if (arr is null || arr.Count == 0)
            return arr?.ToJsonString() ?? "[]";

        var commentIds = new List<string>();
        foreach (var node in arr)
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue("id", out var idNode) || idNode is not JsonValue idVal)
                continue;
            var cid = idVal.GetValue<string>()?.Trim() ?? "";
            if (cid.Length > 0)
                commentIds.Add(cid);
        }

        if (commentIds.Count == 0)
            return arr.ToJsonString();

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

        foreach (var node in arr)
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue("id", out var idNode) || idNode is not JsonValue idVal)
                continue;
            var cid = idVal.GetValue<string>()?.Trim() ?? "";
            if (cid.Length == 0)
                continue;
            o["likeCount"] = likeCounts.GetValueOrDefault(cid, 0);
            o["viewerLiked"] = viewerLikedIds is not null && viewerLikedIds.Contains(cid);
        }

        return arr.ToJsonString();
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
