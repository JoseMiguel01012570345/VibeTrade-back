using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Recommendations;

public interface IGuestRecommendationService
{
    Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        int cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recomendaciones para invitado (sin UserAccount): usa señales locales (visitas/clicks)
/// guardadas en <see cref="IGuestInteractionStore"/> + popularidad global.
/// </summary>
public sealed class GuestRecommendationService(
    AppDbContext db,
    IGuestInteractionStore guestStore,
    IOfferEngagementService offerEngagement)
    : IGuestRecommendationService
{
    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        int cursor,
        CancellationToken cancellationToken = default)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length == 0)
            return RecommendationBatchResponse.Empty(RecommendationService.DefaultBatchSize, RecommendationService.ScoreThreshold);

        var batchSize = Math.Clamp(take, 1, RecommendationService.MaxBatchSize);

        // Candidatos: como invitado, no hay "mis tiendas", así que no filtramos por owner.
        var candidates = await LoadCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
            return RecommendationBatchResponse.Empty(batchSize, RecommendationService.ScoreThreshold);

        var now = DateTimeOffset.UtcNow;
        var guestEvents = guestStore.GetRecent(gid, max: 250);

        var direct = new Dictionary<string, double>(StringComparer.Ordinal);
        var category = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (offerId, eventType) in guestEvents)
        {
            var w = eventType == "chat_start" ? 3d : eventType == "inquiry" ? 2d : 1d;
            AddWeight(direct, offerId, w);
            if (candidates.TryGetValue(offerId, out var c))
                AddWeight(category, c.Category, w);
        }

        var maxDirect = MaxOrZero(direct.Values);
        var maxCategory = MaxOrZero(category.Values);
        var maxPopularity = MaxOrZero(candidates.Values.Select(c => c.PopularityWeight));
        var random = new Random(HashCode.Combine(gid, DateOnly.FromDateTime(now.UtcDateTime).GetHashCode()));

        var scored = candidates.Values
            .Select(c =>
            {
                var categoryMatch = category.ContainsKey(c.Category) ? 1d : 0d;
                var directInterest = Normalize(GetWeight(direct, c.OfferId), maxDirect);
                var categoryInterest = Normalize(GetWeight(category, c.Category), maxCategory);
                var userInterest = Math.Max(directInterest, categoryInterest);
                var popularity = Normalize(c.PopularityWeight, maxPopularity);
                var recency = ComputeRecency(c.UpdatedAt, now);
                var trustScore = Normalize(c.TrustScore, 100d);

                var score =
                    (0.35d * categoryMatch)
                    + (0.30d * userInterest)
                    + (0.20d * popularity)
                    + (0.10d * recency)
                    + (0.05d * trustScore);

                if (c.TrustScore < 40)
                    score *= 0.2d;

                return new ScoredCandidate(c.OfferId, Clamp01(score), random.NextDouble());
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RandomTie)
            .Select(x => x.OfferId)
            .ToList();

        var page = BuildPage(scored, candidates, batchSize, cursor);
        var offers = await BuildOffersJsonForIdsAsync(page.OfferIds, cancellationToken);
        await offerEngagement.EnrichOffersJsonAsync(offers, "g:" + gid, cancellationToken);
        var storeBadges = await BuildStoreBadgesJsonAsync(page.OfferIds, page.RecommendedStoreIds, candidates, cancellationToken);
        return page with { Offers = offers, StoreBadges = storeBadges };
    }

    private async Task<JsonObject> BuildStoreBadgesJsonAsync(
        IReadOnlyList<string> pageOfferIds,
        IReadOnlyList<string> recommendedStoreIds,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oid in pageOfferIds)
        {
            if (candidates.TryGetValue(oid, out var c))
                ids.Add(c.StoreId);
        }
        foreach (var sid in recommendedStoreIds)
            ids.Add(sid);
        if (ids.Count == 0)
            return new JsonObject();

        var rows = await db.Stores.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);
        var o = new JsonObject();
        foreach (var row in rows)
            o[row.Id] = MarketCatalogStoreBadgeJson.FromStoreRow(row);
        return o;
    }

    private sealed record OfferCandidate(
        string OfferId,
        string StoreId,
        string Category,
        int TrustScore,
        DateTimeOffset UpdatedAt,
        double PopularityWeight);

    private sealed record ScoredCandidate(string OfferId, double Score, double RandomTie);

    private async Task<Dictionary<string, OfferCandidate>> LoadCandidatesAsync(CancellationToken cancellationToken)
    {
        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published)
            .Select(p => new { p.Id, p.StoreId, p.Category, p.UpdatedAt, p.PopularityWeight, TrustScore = p.Store.TrustScore })
            .ToListAsync(cancellationToken);

        var services = await db.StoreServices.AsNoTracking()
            .Where(s => s.Published == null || s.Published == true)
            .Select(s => new { s.Id, s.StoreId, s.Category, s.UpdatedAt, s.PopularityWeight, TrustScore = s.Store.TrustScore })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, OfferCandidate>(StringComparer.Ordinal);
        foreach (var p in products)
            map[p.Id] = new OfferCandidate(p.Id, p.StoreId, p.Category ?? "", p.TrustScore, p.UpdatedAt, p.PopularityWeight);
        foreach (var s in services)
            map[s.Id] = new OfferCandidate(s.Id, s.StoreId, s.Category ?? "", s.TrustScore, s.UpdatedAt, s.PopularityWeight);
        return map;
    }

    private static RecommendationBatchResponse BuildPage(
        IReadOnlyList<string> orderedIds,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        int take,
        int cursor)
    {
        if (orderedIds.Count == 0)
            return RecommendationBatchResponse.Empty(take, RecommendationService.ScoreThreshold);

        var start = cursor;
        if (start < 0 || start >= orderedIds.Count)
            start = 0;

        var count = Math.Min(take, orderedIds.Count - start);
        var page = orderedIds.Skip(start).Take(count).ToArray();
        var nextCursor = start + count;
        var wrapped = false;
        if (nextCursor >= orderedIds.Count)
        {
            nextCursor = 0;
            wrapped = true;
        }

        // guest: recomendación simple de tiendas según ventana actual.
        var storeRaw = new Dictionary<string, double>(StringComparer.Ordinal);
        var denom = 0d;
        var lengthFeed = orderedIds.Count;
        for (var i = 0; i < page.Length; i++)
        {
            var pos = start + i;
            var w = lengthFeed - pos;
            denom += w;
            if (!candidates.TryGetValue(page[i], out var c))
                continue;
            storeRaw[c.StoreId] = storeRaw.GetValueOrDefault(c.StoreId) + w;
        }
        var stores = denom <= 0d
            ? Array.Empty<string>()
            : storeRaw.OrderByDescending(kv => kv.Value / denom)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(take)
                .Select(kv => kv.Key)
                .ToArray();

        return new RecommendationBatchResponse(
            page,
            new JsonObject(),
            nextCursor,
            orderedIds.Count,
            take,
            RecommendationService.ScoreThreshold,
            wrapped,
            stores,
            new JsonObject());
    }

    private async Task<JsonObject> BuildOffersJsonForIdsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new JsonObject();

        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => idSet.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => idSet.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var offers = new JsonObject();
        foreach (var p in products)
            offers[p.Id] = MarketCatalogOfferJsonBuilder.ProductRowToOfferJson(p);
        foreach (var s in services)
            offers[s.Id] = MarketCatalogOfferJsonBuilder.ServiceRowToOfferJson(s);
        return offers;
    }

    private static void AddWeight(IDictionary<string, double> target, string key, double weight)
    {
        if (string.IsNullOrWhiteSpace(key) || weight <= 0d)
            return;
        if (target.TryGetValue(key, out var existing))
            target[key] = existing + weight;
        else
            target[key] = weight;
    }

    private static double GetWeight(IReadOnlyDictionary<string, double> source, string key) =>
        source.TryGetValue(key, out var value) ? value : 0d;

    private static double MaxOrZero(IEnumerable<double> values)
    {
        var max = 0d;
        foreach (var value in values)
        {
            if (value > max)
                max = value;
        }
        return max;
    }

    private static double Normalize(double value, double max) =>
        max <= 0d ? 0d : Clamp01(value / max);

    private static double ComputeRecency(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var ageDays = Math.Max(0d, (now - updatedAt).TotalDays);
        return Clamp01(1d - (ageDays / 30d));
    }

    private static double Clamp01(double value) =>
        value switch
        {
            < 0d => 0d,
            > 1d => 1d,
            _ => value,
        };
}

