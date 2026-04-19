using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Recommendations;

public sealed class RecommendationService(
    AppDbContext db,
    IOfferEngagementService offerEngagement,
    IOfferPopularityWeightService popularityWeight) : IRecommendationService
{
    public const int DefaultBatchSize = 20;
    public const int MaxBatchSize = 20;
    /// <summary>
    /// Máximo de índices en la lista rankeada para cursores de recomendación; <c>nextCursor</c> no supera este valor.
    /// </summary>
    public const int MaxRecommendationFeedLength = 100;
    public const double ScoreThreshold = 0.35d;

    /// <summary>
    /// Copia como mucho los primeros <see cref="MaxRecommendationFeedLength"/> ids (nunca devuelve la lista completa por referencia).
    /// </summary>
    public static string[] CapOrderedIdsForPaging(IReadOnlyList<string> orderedIds)
    {
        if (orderedIds is null || orderedIds.Count == 0)
            return [];
        var n = Math.Min(orderedIds.Count, MaxRecommendationFeedLength);
        var arr = new string[n];
        for (var i = 0; i < n; i++)
            arr[i] = orderedIds[i]!;
        return arr;
    }

    /// <summary>Garantiza <c>nextCursor</c> y <c>totalAvailable</c> acotados al feed máximo.</summary>
    public static (int NextCursor, int TotalAvailable) ClampBatchCursorMeta(
        int nextCursor,
        int feedCount)
    {
        var total = Math.Min(Math.Max(0, feedCount), MaxRecommendationFeedLength);
        var next = Math.Min(Math.Max(0, nextCursor), MaxRecommendationFeedLength);
        return (next, total);
    }
    private const int TrustPenaltyThreshold = 40;

    private const double LikeOfferWeight = 1.0d;
    private const double LikeCommentMultiplier = 0.25d;
    private const double PopularityTrustGamma = 1.6d;

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
        var storeBadges = await BuildStoreBadgesJsonAsync(page.OfferIds, page.RecommendedStoreIds, candidates, cancellationToken);
        return page with { Offers = offers, StoreBadges = storeBadges };
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
        await popularityWeight.RecomputeAsync(pid, cancellationToken);
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
        var (relevantEvents, interactionSince) =
            await LoadInteractionSignalsAsync(contacts.RelevantUserIds, now, cancellationToken);

        var popularityWeights = candidates.Values.ToDictionary(
            c => c.OfferId,
            c => c.PopularityWeight,
            StringComparer.Ordinal);

        var (userEvents, contactEvents) = SplitEventsForViewer(viewer.Id, contacts.ContactSet, relevantEvents);
        var userWeights = BuildUserAndSavedWeights(viewer, candidates, userEvents);

        var userOfferLikes = await LoadOfferLikeCountsByOfferAsync(
            "u:" + viewer.Id,
            interactionSince,
            cancellationToken);
        var userCommentLikes = await LoadCommentLikeCountsByOfferAsync(
            "u:" + viewer.Id,
            interactionSince,
            cancellationToken);
        AddUserLikesToInterest(userWeights, userOfferLikes, userCommentLikes, candidates);

        if (contacts.ContactIds.Count > 0)
        {
            var contactKeys = contacts.ContactIds.Select(c => "u:" + c).ToList();
            var contactOfferLikes = await LoadOfferLikeCountsByLikerKeysAsync(
                contactKeys,
                interactionSince,
                cancellationToken);
            var contactCommentLikes = await LoadCommentLikeCountsByLikerKeysAsync(
                contactKeys,
                interactionSince,
                cancellationToken);
            AddContactInterestWithTrust(
                userWeights,
                contactEvents,
                contactOfferLikes,
                contactCommentLikes,
                candidates);
        }

        ApplyInquiryFallbacksToPopularity(candidates, popularityWeights);

        var scored = ScoreCandidates(viewer.Id, now, candidates, userWeights, popularityWeights);
        scored = OrderScoredList(scored);
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

    private async Task<(List<InteractionPoint> RelevantEvents, DateTimeOffset InteractionSince)>
        LoadInteractionSignalsAsync(
            IReadOnlyList<string> relevantUserIds,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var interactionSince = now.AddDays(-30);

        var relevantEvents = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => relevantUserIds.Contains(x.UserId) && x.CreatedAt >= interactionSince)
            .Select(x => new InteractionPoint(x.UserId, x.OfferId, x.EventType, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return (relevantEvents, interactionSince);
    }

    private async Task<Dictionary<string, int>> LoadOfferLikeCountsByOfferAsync(
        string likerKey,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        await db.OfferLikes.AsNoTracking()
            .Where(x => x.LikerKey == likerKey && x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

    private async Task<Dictionary<string, int>> LoadCommentLikeCountsByOfferAsync(
        string likerKey,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => x.LikerKey == likerKey && x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

    private async Task<Dictionary<string, int>> LoadOfferLikeCountsByLikerKeysAsync(
        IReadOnlyList<string> likerKeys,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (likerKeys.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return await db.OfferLikes.AsNoTracking()
            .Where(x => likerKeys.Contains(x.LikerKey) && x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);
    }

    private async Task<Dictionary<string, int>> LoadCommentLikeCountsByLikerKeysAsync(
        IReadOnlyList<string> likerKeys,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (likerKeys.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => likerKeys.Contains(x.LikerKey) && x.CreatedAtUtc >= since)
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);
    }

    private static (List<InteractionPoint> UserEvents, List<InteractionPoint> ContactEvents) SplitEventsForViewer(
        string viewerId,
        HashSet<string> contactSet,
        IReadOnlyList<InteractionPoint> relevantEvents)
    {
        var userEvents = relevantEvents
            .Where(x => string.Equals(x.UserId, viewerId, StringComparison.Ordinal))
            .ToList();
        var contactEvents = relevantEvents
            .Where(x => contactSet.Contains(x.UserId))
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

    private static void AddUserLikesToInterest(
        UserInterestWeights userWeights,
        IReadOnlyDictionary<string, int> offerLikeCounts,
        IReadOnlyDictionary<string, int> commentLikeCounts,
        IReadOnlyDictionary<string, OfferCandidate> candidates)
    {
        foreach (var kv in offerLikeCounts)
        {
            var w = LikeOfferWeight * kv.Value;
            AddWeight(userWeights.DirectByOfferId, kv.Key, w);
            if (candidates.TryGetValue(kv.Key, out var c))
                AddWeight(userWeights.CategoryByName, c.Category, w);
        }

        foreach (var kv in commentLikeCounts)
        {
            var w = LikeOfferWeight * LikeCommentMultiplier * kv.Value;
            AddWeight(userWeights.DirectByOfferId, kv.Key, w);
            if (candidates.TryGetValue(kv.Key, out var c))
                AddWeight(userWeights.CategoryByName, c.Category, w);
        }
    }

    private static void AddContactInterestWithTrust(
        UserInterestWeights userWeights,
        IReadOnlyList<InteractionPoint> contactEvents,
        IReadOnlyDictionary<string, int> contactOfferLikeCounts,
        IReadOnlyDictionary<string, int> contactCommentLikeCounts,
        IReadOnlyDictionary<string, OfferCandidate> candidates)
    {
        foreach (var ev in contactEvents)
        {
            if (!candidates.TryGetValue(ev.OfferId, out var c))
                continue;
            var tn = TrustNorm(c.TrustScore);
            var w = EventWeight(ev.EventType) * tn;
            AddWeight(userWeights.DirectByOfferId, ev.OfferId, w);
            AddWeight(userWeights.CategoryByName, c.Category, w);
        }

        foreach (var kv in contactOfferLikeCounts)
        {
            if (!candidates.TryGetValue(kv.Key, out var c))
                continue;
            var tn = TrustNorm(c.TrustScore);
            var w = LikeOfferWeight * kv.Value * tn;
            AddWeight(userWeights.DirectByOfferId, kv.Key, w);
            AddWeight(userWeights.CategoryByName, c.Category, w);
        }

        foreach (var kv in contactCommentLikeCounts)
        {
            if (!candidates.TryGetValue(kv.Key, out var c))
                continue;
            var tn = TrustNorm(c.TrustScore);
            var w = LikeOfferWeight * LikeCommentMultiplier * kv.Value * tn;
            AddWeight(userWeights.DirectByOfferId, kv.Key, w);
            AddWeight(userWeights.CategoryByName, c.Category, w);
        }
    }

    private static double TrustNorm(int trustScore) =>
        Clamp01(trustScore / 100d);

    private static double PopularityTrustMultiplier(int trustScore) =>
        Math.Pow(Clamp01(trustScore / 100d), PopularityTrustGamma);

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
        UserInterestWeights userWeights,
        IReadOnlyDictionary<string, double> popularityWeightsRaw)
    {
        var maxDirect = MaxOrZero(userWeights.DirectByOfferId.Values);
        var maxCategory = MaxOrZero(userWeights.CategoryByName.Values);
        var maxPopularity = MaxOrZero(candidates.Values.Select(c =>
            GetWeight(popularityWeightsRaw, c.OfferId) * PopularityTrustMultiplier(c.TrustScore)));
        var random = new Random(HashCode.Combine(viewerId, DateOnly.FromDateTime(now.UtcDateTime).GetHashCode()));

        return candidates.Values
            .Select(candidate =>
            {
                var categoryMatch = userWeights.CategoryByName.ContainsKey(candidate.Category) ? 1d : 0d;
                var directInterest = Normalize(GetWeight(userWeights.DirectByOfferId, candidate.OfferId), maxDirect);
                var categoryInterest = Normalize(GetWeight(userWeights.CategoryByName, candidate.Category), maxCategory);
                var userInterest = Math.Max(directInterest, categoryInterest);
                var rawPop = GetWeight(popularityWeightsRaw, candidate.OfferId);
                var popularity = Normalize(
                    rawPop * PopularityTrustMultiplier(candidate.TrustScore),
                    maxPopularity);
                var recency = ComputeRecency(candidate.UpdatedAt, now);
                var trustScore = Normalize(candidate.TrustScore, 100d);

                var score =
                    (0.30d * categoryMatch)
                    + (0.25d * userInterest)
                    + (0.20d * popularity)
                    + (0.10d * recency)
                    + (0.15d * trustScore);

                if (candidate.TrustScore < TrustPenaltyThreshold)
                    score *= 0.2d;

                return new ScoredCandidate(
                    candidate.OfferId,
                    Clamp01(score),
                    false,
                    random.NextDouble());
            })
            .ToList();
    }

    private static List<ScoredCandidate> OrderScoredList(List<ScoredCandidate> scored) =>
        scored.OrderByDescending(x => x.Score).ThenBy(x => x.RandomTie).ToList();

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
                p.PopularityWeight,
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
                s.PopularityWeight,
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
                CountQaItems(item.OfferQaJson),
                item.PopularityWeight);
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
                CountQaItems(item.OfferQaJson),
                item.PopularityWeight);
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
        var feed = CapOrderedIdsForPaging(orderedIds);
        if (feed.Length == 0)
            return RecommendationBatchResponse.Empty(take, ScoreThreshold);

        var start = cursor < 0 ? 0 : cursor;
        if (start >= feed.Length)
            start %= feed.Length;

        var count = Math.Min(take, feed.Length - start);
        var page = feed.Skip(start).Take(count).ToArray();
        var nextCursor = start + count;
        var wrapped = false;
        if (nextCursor >= feed.Length)
        {
            nextCursor = 0;
            wrapped = true;
        }

        var (cursorOut, totalOut) = ClampBatchCursorMeta(nextCursor, feed.Length);

        var recommendedStores = RankRecommendedStoreIds(feed, page, start, candidates, take);

        return new RecommendationBatchResponse(
            page,
            new JsonObject(),
            cursorOut,
            totalOut,
            take,
            ScoreThreshold,
            wrapped,
            recommendedStores,
            new JsonObject());
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

    private sealed record OfferCandidate(
        string OfferId,
        string StoreId,
        string Category,
        string OwnerUserId,
        int TrustScore,
        DateTimeOffset UpdatedAt,
        int InquiryCount,
        double PopularityWeight);

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
