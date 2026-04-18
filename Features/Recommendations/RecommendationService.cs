using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Recommendations;

public sealed class RecommendationService(AppDbContext db, IOfferEngagementService offerEngagement) : IRecommendationService
{
    public const int DefaultBatchSize = 20;
    public const int MaxBatchSize = 20;
    public const double ScoreThreshold = 0.35d;
    private const int TrustPenaltyThreshold = 40;

    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string viewerUserId,
        int take,
        int cursor,
        CancellationToken cancellationToken = default)
    {
        var userId = viewerUserId.Trim();
        if (userId.Length == 0)
            return RecommendationBatchResponse.Empty(DefaultBatchSize, ScoreThreshold);

        var viewer = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (viewer is null)
            return RecommendationBatchResponse.Empty(DefaultBatchSize, ScoreThreshold);

        var batchSize = Math.Clamp(take, 1, MaxBatchSize);
        var (orderedIds, candidates) = await BuildOrderedFeedAsync(viewer, cancellationToken);
        var page = BuildPage(orderedIds, candidates, batchSize, cursor);
        var offers = await BuildOffersJsonForIdsAsync(page.OfferIds, cancellationToken);
        await offerEngagement.EnrichOffersJsonAsync(offers, "u:" + userId, cancellationToken);
        return page with { Offers = offers };
    }

    public async Task RecordInteractionAsync(
        string userId,
        string offerId,
        RecommendationInteractionType eventType,
        CancellationToken cancellationToken = default)
    {
        var viewerId = userId.Trim();
        var pid = offerId.Trim();
        if (viewerId.Length == 0 || pid.Length == 0)
            return;

        if (!await OfferExistsAsync(pid, cancellationToken))
            return;

        db.UserOfferInteractions.Add(new UserOfferInteractionRow
        {
            Id = "uoi_" + Guid.NewGuid().ToString("N"),
            UserId = viewerId,
            OfferId = pid,
            EventType = ToStorageValue(eventType),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(IReadOnlyList<string> OrderedIds, Dictionary<string, OfferCandidate> Candidates)>
        BuildOrderedFeedAsync(
            UserAccount viewer,
            CancellationToken cancellationToken)
    {
        var candidates = await LoadCandidatesAsync(viewer.Id, cancellationToken);
        if (candidates.Count == 0)
            return (Array.Empty<string>(), candidates);

        var now = DateTimeOffset.UtcNow;
        var contacts = await LoadViewerContactsAsync(viewer.Id, cancellationToken);
        var (relevantEvents, popularityWeights) =
            await LoadInteractionSignalsAsync(contacts.RelevantUserIds, now, cancellationToken);

        var (userEvents, contactEvents) = SplitEventsForViewer(viewer.Id, contacts.ContactSet, now, relevantEvents);
        var userWeights = BuildUserAndSavedWeights(viewer, candidates, userEvents);
        var contactSignals = BuildContactSignalWeights(contactEvents, candidates);

        ApplyInquiryFallbacksToPopularity(candidates, popularityWeights);

        var scored = ScoreCandidates(
            viewer.Id,
            now,
            candidates,
            contacts.ContactIds,
            userWeights,
            contactSignals,
            popularityWeights);

        scored = OrderScoredList(scored, contactSignals.SeedIds);
        var orderedIds = DedupeOrderedIdsByThreshold(scored);
        return (orderedIds, candidates);
    }

    private async Task<ViewerContacts> LoadViewerContactsAsync(string viewerId, CancellationToken cancellationToken)
    {
        var contactIds = await db.UserContacts.AsNoTracking()
            .Where(c => c.OwnerUserId == viewerId)
            .Select(c => c.ContactUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var contactSet = contactIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(contactIds, StringComparer.Ordinal);

        var relevantUserIds = new List<string>(capacity: 1 + contactIds.Count) { viewerId };
        relevantUserIds.AddRange(contactIds);
        return new ViewerContacts(contactIds, contactSet, relevantUserIds);
    }

    private async Task<(List<InteractionPoint> RelevantEvents, Dictionary<string, double> PopularityWeights)>
        LoadInteractionSignalsAsync(
            IReadOnlyList<string> relevantUserIds,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var interactionSince = now.AddDays(-45);
        var popularitySince = now.AddDays(-30);

        // No usar Task.WhenAll: una sola instancia de DbContext no admite dos consultas a la vez.
        var relevantEvents = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => relevantUserIds.Contains(x.UserId) && x.CreatedAt >= interactionSince)
            .Select(x => new InteractionPoint(x.UserId, x.OfferId, x.EventType, x.CreatedAt))
            .ToListAsync(cancellationToken);

        var popularityWeights = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => x.CreatedAt >= popularitySince)
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

        return (relevantEvents, popularityWeights);
    }

    private static (List<InteractionPoint> UserEvents, List<InteractionPoint> ContactEvents) SplitEventsForViewer(
        string viewerId,
        HashSet<string> contactSet,
        DateTimeOffset now,
        IReadOnlyList<InteractionPoint> relevantEvents)
    {
        var contactSince = now.AddDays(-15);
        var userEvents = relevantEvents
            .Where(x => string.Equals(x.UserId, viewerId, StringComparison.Ordinal))
            .ToList();
        var contactEvents = relevantEvents
            .Where(x => contactSet.Contains(x.UserId) && x.CreatedAt >= contactSince)
            .ToList();
        return (userEvents, contactEvents);
    }

    private static UserInterestWeights BuildUserAndSavedWeights(
        UserAccount viewer,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        IReadOnlyList<InteractionPoint> userEvents)
    {
        var direct = new Dictionary<string, double>(StringComparer.Ordinal);
        var category = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in userEvents)
        {
            var weight = EventWeight(ev.EventType);
            AddWeight(direct, ev.OfferId, weight);
            if (candidates.TryGetValue(ev.OfferId, out var candidate))
                AddWeight(category, candidate.Category, weight);
        }

        foreach (var savedId in ParseSavedOfferIds(viewer.SavedOfferIdsJson))
        {
            AddWeight(direct, savedId, 1.5d);
            if (candidates.TryGetValue(savedId, out var candidate))
                AddWeight(category, candidate.Category, 0.75d);
        }

        return new UserInterestWeights(direct, category);
    }

    private static ContactGraphSignals BuildContactSignalWeights(
        IReadOnlyList<InteractionPoint> contactEvents,
        IReadOnlyDictionary<string, OfferCandidate> candidates)
    {
        var contactSignalWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var ev in contactEvents)
            AddWeight(contactSignalWeights, ev.OfferId, EventWeight(ev.EventType));

        var seedIds = contactEvents
            .GroupBy(x => x.OfferId, StringComparer.Ordinal)
            .Select(g => new
            {
                OfferId = g.Key,
                Weight = g.Sum(x => EventWeight(x.EventType)),
                LatestAt = g.Max(x => x.CreatedAt),
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.LatestAt)
            .Select(x => x.OfferId)
            .Where(candidates.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);

        return new ContactGraphSignals(contactSignalWeights, seedIds);
    }

    private static void ApplyInquiryFallbacksToPopularity(
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        Dictionary<string, double> popularityWeights)
    {
        foreach (var candidate in candidates.Values)
        {
            if (!popularityWeights.ContainsKey(candidate.OfferId))
                popularityWeights[candidate.OfferId] = candidate.InquiryCount * 2d;
            else
                popularityWeights[candidate.OfferId] += candidate.InquiryCount * 2d;
        }
    }

    private static List<ScoredCandidate> ScoreCandidates(
        string viewerId,
        DateTimeOffset now,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        IReadOnlyList<string> contactIds,
        UserInterestWeights userWeights,
        ContactGraphSignals contactSignals,
        IReadOnlyDictionary<string, double> popularityWeights)
    {
        var maxDirect = MaxOrZero(userWeights.DirectByOfferId.Values);
        var maxCategory = MaxOrZero(userWeights.CategoryByName.Values);
        var maxPopularity = MaxOrZero(popularityWeights.Values);
        var contactDenominator = Math.Max(1, contactIds.Count) * 3d;
        var random = new Random(HashCode.Combine(viewerId, DateOnly.FromDateTime(now.UtcDateTime).GetHashCode()));

        return candidates.Values
            .Select(candidate =>
            {
                var categoryMatch = userWeights.CategoryByName.ContainsKey(candidate.Category) ? 1d : 0d;
                var directInterest = Normalize(GetWeight(userWeights.DirectByOfferId, candidate.OfferId), maxDirect);
                var categoryInterest = Normalize(GetWeight(userWeights.CategoryByName, candidate.Category), maxCategory);
                var userInterest = Math.Max(directInterest, categoryInterest);
                var popularity = Normalize(GetWeight(popularityWeights, candidate.OfferId), maxPopularity);
                var recency = ComputeRecency(candidate.UpdatedAt, now);
                var trustScore = Normalize(candidate.TrustScore, 100d);
                var contactGraphScore = contactIds.Count == 0
                    ? 0d
                    : Clamp01(GetWeight(contactSignals.SignalByOfferId, candidate.OfferId) / contactDenominator);

                var score =
                    (0.30d * categoryMatch)
                    + (0.25d * userInterest)
                    + (0.20d * popularity)
                    + (0.10d * recency)
                    + (0.10d * trustScore)
                    + (0.05d * contactGraphScore);

                if (candidate.TrustScore < TrustPenaltyThreshold)
                    score *= 0.2d;
                if (contactSignals.SeedIds.Contains(candidate.OfferId))
                    score += 0.08d;

                return new ScoredCandidate(
                    candidate.OfferId,
                    Clamp01(score),
                    contactSignals.SeedIds.Contains(candidate.OfferId),
                    random.NextDouble());
            })
            .OrderByDescending(x => x.IsContactSeed)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.RandomTie)
            .ToList();
    }

    private static List<ScoredCandidate> OrderScoredList(List<ScoredCandidate> scored, HashSet<string> contactSeedIds)
    {
        if (contactSeedIds.Count != 0)
            return scored;
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RandomTie)
            .ToList();
    }

    private static List<string> DedupeOrderedIdsByThreshold(IReadOnlyList<ScoredCandidate> scored)
    {
        var orderedIds = new List<string>(scored.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in scored.Where(x => x.Score >= ScoreThreshold)
                     .Concat(scored.Where(x => x.Score < ScoreThreshold)))
        {
            if (seen.Add(item.OfferId))
                orderedIds.Add(item.OfferId);
        }

        return orderedIds;
    }

    private async Task<Dictionary<string, OfferCandidate>> LoadCandidatesAsync(
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published && p.Store.OwnerUserId != viewerUserId)
            .Select(p => new
            {
                p.Id,
                p.StoreId,
                p.Category,
                p.UpdatedAt,
                p.OfferQaJson,
                OwnerUserId = p.Store.OwnerUserId,
                TrustScore = p.Store.TrustScore,
            })
            .ToListAsync(cancellationToken);

        var services = await db.StoreServices.AsNoTracking()
            .Where(s => (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
            .Select(s => new
            {
                s.Id,
                s.StoreId,
                s.Category,
                s.UpdatedAt,
                s.OfferQaJson,
                OwnerUserId = s.Store.OwnerUserId,
                TrustScore = s.Store.TrustScore,
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, OfferCandidate>(StringComparer.Ordinal);
        foreach (var item in products)
        {
            map[item.Id] = new OfferCandidate(
                item.Id,
                item.StoreId,
                item.Category ?? "",
                item.OwnerUserId,
                item.TrustScore,
                item.UpdatedAt,
                CountQaItems(item.OfferQaJson));
        }

        foreach (var item in services)
        {
            map[item.Id] = new OfferCandidate(
                item.Id,
                item.StoreId,
                item.Category ?? "",
                item.OwnerUserId,
                item.TrustScore,
                item.UpdatedAt,
                CountQaItems(item.OfferQaJson));
        }

        return map;
    }

    private async Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken)
    {
        var inProducts = await db.StoreProducts.AsNoTracking()
            .AnyAsync(p => p.Id == offerId, cancellationToken);
        if (inProducts)
            return true;

        return await db.StoreServices.AsNoTracking()
            .AnyAsync(s => s.Id == offerId, cancellationToken);
    }

    private static RecommendationBatchResponse BuildPage(
        IReadOnlyList<string> orderedIds,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        int take,
        int cursor)
    {
        if (orderedIds.Count == 0)
            return RecommendationBatchResponse.Empty(take, ScoreThreshold);

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

        var recommendedStores = RankRecommendedStoreIds(orderedIds, page, start, candidates, take);

        return new RecommendationBatchResponse(
            page,
            new JsonObject(),
            nextCursor,
            orderedIds.Count,
            take,
            ScoreThreshold,
            wrapped,
            recommendedStores);
    }

    /// <summary>
    /// Por ventana [start, start + page.Count): peso (lengthFeed - pos) por posición; score tienda = suma de pesos
    /// de sus ofertas en la ventana, normalizado por la suma total de pesos de la ventana. Orden desc, hasta take ids.
    /// </summary>
    private static string[] RankRecommendedStoreIds(
        IReadOnlyList<string> orderedIds,
        IReadOnlyList<string> pageOfferIds,
        int pageStart,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        int maxStores)
    {
        var lengthFeed = orderedIds.Count;
        if (lengthFeed == 0 || pageOfferIds.Count == 0 || maxStores <= 0)
            return Array.Empty<string>();

        var storeRaw = new Dictionary<string, double>(StringComparer.Ordinal);
        var denom = 0d;
        for (var i = 0; i < pageOfferIds.Count; i++)
        {
            var pos = pageStart + i;
            var w = lengthFeed - pos;
            denom += w;
            var offerId = pageOfferIds[i];
            if (!candidates.TryGetValue(offerId, out var c))
                continue;
            storeRaw[c.StoreId] = storeRaw.GetValueOrDefault(c.StoreId) + w;
        }

        if (denom <= 0d)
            return Array.Empty<string>();

        return storeRaw
            .OrderByDescending(kv => kv.Value / denom)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(maxStores)
            .Select(kv => kv.Key)
            .ToArray();
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

    private static IEnumerable<string> ParseSavedOfferIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int CountQaItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string ToStorageValue(RecommendationInteractionType eventType) =>
        eventType switch
        {
            RecommendationInteractionType.Click => "click",
            RecommendationInteractionType.Inquiry => "inquiry",
            RecommendationInteractionType.ChatStart => "chat_start",
            _ => "click",
        };

    private static double EventWeight(string eventType) =>
        eventType switch
        {
            "chat_start" => 3d,
            "inquiry" => 2d,
            _ => 1d,
        };

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

    private sealed record ViewerContacts(
        List<string> ContactIds,
        HashSet<string> ContactSet,
        List<string> RelevantUserIds);

    private sealed record UserInterestWeights(
        Dictionary<string, double> DirectByOfferId,
        Dictionary<string, double> CategoryByName);

    private sealed record ContactGraphSignals(
        Dictionary<string, double> SignalByOfferId,
        HashSet<string> SeedIds);

    private sealed record OfferCandidate(
        string OfferId,
        string StoreId,
        string Category,
        string OwnerUserId,
        int TrustScore,
        DateTimeOffset UpdatedAt,
        int InquiryCount);

    private sealed record InteractionPoint(
        string UserId,
        string OfferId,
        string EventType,
        DateTimeOffset CreatedAt);

    private sealed record ScoredCandidate(
        string OfferId,
        double Score,
        bool IsContactSeed,
        double RandomTie);
}
