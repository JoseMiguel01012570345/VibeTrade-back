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
    IOfferPopularityWeightService popularityWeight,
    RecommendationFeedV2 feedV2) : IRecommendationService
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
        var now = DateTimeOffset.UtcNow;
        var contacts = await LoadViewerContactsAsync(viewer.Id, cancellationToken);
        var (relevantEvents, _) =
            await LoadInteractionSignalsAsync(contacts.RelevantUserIds, now, cancellationToken);

        var (userEvents, contactEvents) = SplitEventsForViewer(viewer.Id, contacts.ContactSet, relevantEvents);
        var v2Ids = await feedV2.TryBuildOrderedOfferIdsAsync(
            viewer.Id,
            viewer,
            contacts.ContactIds,
            userEvents,
            contactEvents,
            now,
            ScoreThreshold,
            cancellationToken);

        if (v2Ids is not { Count: > 0 })
            return (Array.Empty<string>(), new Dictionary<string, OfferCandidate>(StringComparer.Ordinal));

        var feed = CapOrderedIdsForPaging(v2Ids);
        var candidates = await LoadCandidatesForOfferIdsAsync(viewer.Id, feed.ToHashSet(StringComparer.Ordinal), cancellationToken);
        return (v2Ids, candidates);
    }

    private async Task<Dictionary<string, OfferCandidate>> LoadCandidatesForOfferIdsAsync(
        string viewerUserId,
        IReadOnlyCollection<string> offerIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, OfferCandidate>(StringComparer.Ordinal);
        if (offerIds.Count == 0)
            return map;

        var idList = offerIds.ToList();
        var products = await db.StoreProducts.AsNoTracking()
            .Include(p => p.Store)
            .Where(p => idList.Contains(p.Id) && p.Published && p.Store.OwnerUserId != viewerUserId)
            .ToListAsync(cancellationToken);

        var services = await db.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .Where(s => idList.Contains(s.Id) && (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
            .ToListAsync(cancellationToken);

        foreach (var item in products)
        {
            map[item.Id] = new OfferCandidate(
                item.Id,
                item.StoreId,
                item.Category ?? "",
                item.Store.OwnerUserId,
                item.Store.TrustScore,
                item.UpdatedAt,
                item.OfferQa.Count,
                item.PopularityWeight);
        }

        foreach (var item in services)
        {
            map[item.Id] = new OfferCandidate(
                item.Id,
                item.StoreId,
                item.Category ?? "",
                item.Store.OwnerUserId,
                item.Store.TrustScore,
                item.UpdatedAt,
                item.OfferQa.Count,
                item.PopularityWeight);
        }

        return map;
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
        var interactionSince = now.AddDays(-7);

        var relevantEvents = await db.UserOfferInteractions.AsNoTracking()
            .Where(x => relevantUserIds.Contains(x.UserId) && x.CreatedAt >= interactionSince)
            .Select(x => new InteractionPoint(x.UserId, x.OfferId, x.EventType, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return (relevantEvents, interactionSince);
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

    private static string ToStorageValue(RecommendationInteractionType eventType) =>
        eventType switch
        {
            RecommendationInteractionType.Click => "click",
            RecommendationInteractionType.Inquiry => "inquiry",
            RecommendationInteractionType.ChatStart => "chat_start",
            _ => "click",
        };
}
