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
    public const double ScoreThreshold = 0.35d;

    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string viewerUserId,
        int take,
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
        var (orderedIds, candidates) = await BuildOrderedFeedAsync(viewer, batchSize, cancellationToken);
        var pageIds = orderedIds.Where(id => candidates.ContainsKey(id)).ToArray();
        if (pageIds.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, ScoreThreshold);

        var offers = await BuildOffersJsonForIdsAsync(pageIds, cancellationToken);
        await offerEngagement.EnrichOffersJsonAsync(offers, "u:" + userId, cancellationToken);
        var storeBadges = await BuildStoreBadgesJsonAsync(pageIds, candidates, cancellationToken);
        return new RecommendationBatchResponse(offers, storeBadges, batchSize, ScoreThreshold);
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

    private async Task<(string[] OrderedIds, Dictionary<string, OfferCandidate> Candidates)>
        BuildOrderedFeedAsync(
            UserAccount viewer,
            int maxOffers,
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
            maxOffers,
            cancellationToken);

        if (v2Ids is not { Count: > 0 })
            return ([], new Dictionary<string, OfferCandidate>(StringComparer.Ordinal));

        var idList = v2Ids.Select(id => id.Trim()).Where(id => id.Length > 0).ToArray();
        var candidates = await LoadCandidatesForOfferIdsAsync(viewer.Id, idList.ToHashSet(StringComparer.Ordinal), cancellationToken);
        return (idList, candidates);
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

    private async Task<JsonObject> BuildStoreBadgesJsonAsync(
        IReadOnlyList<string> pageOfferIds,
        IReadOnlyDictionary<string, OfferCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oid in pageOfferIds)
        {
            if (candidates.TryGetValue(oid, out var c))
                ids.Add(c.StoreId);
        }
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

    private async Task<JsonObject> BuildOffersJsonForIdsAsync(
        IReadOnlyList<string> idsInOrder,
        CancellationToken cancellationToken)
    {
        if (idsInOrder.Count == 0)
            return new JsonObject();

        var idSet = idsInOrder.ToHashSet(StringComparer.Ordinal);
        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => idSet.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => idSet.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var p in products)
            byId[p.Id] = MarketCatalogOfferJsonBuilder.ProductRowToOfferJson(p);
        foreach (var s in services)
            byId[s.Id] = MarketCatalogOfferJsonBuilder.ServiceRowToOfferJson(s);

        var offers = new JsonObject();
        foreach (var id in idsInOrder)
        {
            if (byId.TryGetValue(id, out var node))
                offers[id] = node;
        }
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
