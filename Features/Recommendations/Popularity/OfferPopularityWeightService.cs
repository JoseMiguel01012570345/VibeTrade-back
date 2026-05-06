using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Recommendations.Popularity;

public sealed class OfferPopularityWeightService(AppDbContext db) : IOfferPopularityWeightService
{
    public const int WindowDays = 30;
    private const double LikeOfferWeight = 1.0d;
    private const double LikeCommentMultiplier = 0.25d;

    public async Task RecomputeAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return;

        var since = DateTimeOffset.UtcNow.AddDays(-WindowDays);
        var total = await ComputeRawPopularityForOfferAsync(oid, since, cancellationToken);

        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == oid, cancellationToken);
        if (product is not null)
        {
            product.PopularityWeight = total;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var service = await db.StoreServices.FirstOrDefaultAsync(s => s.Id == oid, cancellationToken);
        if (service is not null)
        {
            service.PopularityWeight = total;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RecomputeAllPublishedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-WindowDays);

        var popularityWeights = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => x.CreatedAt >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new
            {
                OfferId = g.Key,
                Weight = g.Sum(x => x.EventType == "chat_start"
                    ? 3
                    : x.EventType == "inquiry"
                        ? 2
                        : 1),
            })
            .ToDictionaryAsync(x => x.OfferId, x => (double)x.Weight, cancellationToken);

        await AppendLikesAsync(popularityWeights, since, cancellationToken);

        var products = await db.StoreProducts.Where(p => p.Published).ToListAsync(cancellationToken);
        foreach (var p in products)
            p.PopularityWeight = popularityWeights.GetValueOrDefault(p.Id, 0d);

        var services = await db.StoreServices
            .Where(s => s.Published == null || s.Published == true)
            .ToListAsync(cancellationToken);
        foreach (var s in services)
            s.PopularityWeight = popularityWeights.GetValueOrDefault(s.Id, 0d);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<double> ComputeRawPopularityForOfferAsync(
        string offerId,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var interactionWeight = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => x.OfferId == offerId && x.CreatedAt >= since)
            .SumAsync(
                x => x.EventType == "chat_start"
                    ? 3
                    : x.EventType == "inquiry"
                        ? 2
                        : 1,
                cancellationToken);

        var offerLikes = await db.OfferLikes.AsNoTracking()
            .CountAsync(x => x.OfferId == offerId && x.CreatedAtUtc >= since, cancellationToken);

        var commentLikes = await db.OfferQaCommentLikes.AsNoTracking()
            .CountAsync(x => x.OfferId == offerId && x.CreatedAtUtc >= since, cancellationToken);

        return interactionWeight
            + LikeOfferWeight * offerLikes
            + LikeOfferWeight * LikeCommentMultiplier * commentLikes;
    }

    private async Task AppendLikesAsync(
        Dictionary<string, double> popularityWeights,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var offerCounts = await db.OfferLikes.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        var commentCounts = await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        foreach (var kv in offerCounts)
            AddWeight(popularityWeights, kv.Key, LikeOfferWeight * kv.Value);

        foreach (var kv in commentCounts)
            AddWeight(popularityWeights, kv.Key, LikeOfferWeight * LikeCommentMultiplier * kv.Value);
    }

    private static void AddWeight(Dictionary<string, double> target, string key, double weight)
    {
        if (string.IsNullOrWhiteSpace(key) || weight <= 0d)
            return;
        if (target.TryGetValue(key, out var existing))
            target[key] = existing + weight;
        else
            target[key] = weight;
    }
}
