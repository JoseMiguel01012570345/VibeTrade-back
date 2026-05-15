using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Recommendations;

public sealed class RecommendationService(
    AppDbContext db,
    IOfferService offerService,
    IOfferPopularityWeightService popularityWeight,
    RecommendationFeedV2 feedV2) : IRecommendationService
{
    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string viewerUserId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var userId = viewerUserId.Trim();
        if (userId.Length == 0)
            return RecommendationBatchResponse.Empty(RecommendationUtils.DefaultBatchSize, RecommendationUtils.ScoreThreshold);

        var viewer = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (viewer is null)
            return RecommendationBatchResponse.Empty(RecommendationUtils.DefaultBatchSize, RecommendationUtils.ScoreThreshold);

        var batchSize = RecommendationUtils.NormalizeClientTake(take);
        var (orderedIds, candidates) = await BuildOrderedFeedAsync(viewer, batchSize, cancellationToken);
        var pageIds = orderedIds.Where(id => candidates.ContainsKey(id)).ToArray();
        if (pageIds.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, RecommendationUtils.ScoreThreshold);

        var offers = await BuildOffersViewForIdsAsync(pageIds, cancellationToken);
        await offerService.EnrichHomeOffersAsync(offers, "u:" + userId, cancellationToken);
        var storeBadges = await BuildStoreBadgesViewAsync(pageIds, candidates, cancellationToken);
        return new RecommendationBatchResponse(pageIds, offers, storeBadges, batchSize, RecommendationUtils.ScoreThreshold);
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

        if (!await OfferOrEmergentExistsAsync(pid, cancellationToken))
            return;

        db.UserOfferInteractions.Add(new UserOfferInteractionRow
        {
            Id = "uoi_" + Guid.NewGuid().ToString("N"),
            UserId = viewerId,
            OfferId = pid,
            EventType = RecommendationUtils.InteractionTypeToStorageValue(eventType),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        if (!OfferUtils.IsEmergentPublicationId(pid))
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

        var (userEvents, contactEvents) = RecommendationUtils.SplitEventsForViewer(viewer.Id, contacts.ContactSet, relevantEvents);
        var v2Ids = await feedV2.TryBuildOrderedOfferIdsAsync(
            viewer.Id,
            viewer,
            contacts.ContactIds,
            userEvents,
            contactEvents,
            now,
            RecommendationUtils.ScoreThreshold,
            maxOffers,
            cancellationToken);

        var idList = v2Ids is { Count: > 0 }
            ? v2Ids.Select(id => id.Trim()).Where(id => id.Length > 0).ToArray()
            : Array.Empty<string>();

        if (idList.Length == 0)
            return ([], new Dictionary<string, OfferCandidate>(StringComparer.Ordinal));

        var candidates = await RecommendationBatchOfferLoader.LoadOfferCandidatesAsync(
            db, viewer.Id, idList.ToHashSet(StringComparer.Ordinal), cancellationToken);
        return (idList, candidates);
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

    private async Task<bool> OfferOrEmergentExistsAsync(string offerId, CancellationToken cancellationToken)
    {
        if (OfferUtils.IsEmergentPublicationId(offerId))
        {
            return await db.EmergentOffers.AsNoTracking()
                .AnyAsync(e =>
                    e.Id == offerId
                    && e.RetractedAtUtc == null
                    && db.ChatRouteSheets.Any(r =>
                        r.ThreadId == e.ThreadId
                        && r.RouteSheetId == e.RouteSheetId
                        && r.DeletedAtUtc == null
                        && r.PublishedToPlatform),
                    cancellationToken);
        }
        var inProducts = await db.StoreProducts.AsNoTracking()
            .AnyAsync(p => p.Id == offerId, cancellationToken);
        if (inProducts)
            return true;

        return await db.StoreServices.AsNoTracking()
            .AnyAsync(s => s.Id == offerId, cancellationToken);
    }

    private async Task<Dictionary<string, StoreProfileWorkspaceData>> BuildStoreBadgesViewAsync(
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
            return new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);

        var rows = await db.Stores.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);
        var o = new Dictionary<string, StoreProfileWorkspaceData>(StringComparer.Ordinal);
        foreach (var row in rows)
            o[row.Id] = StoreProfileWorkspaceData.FromStoreRow(row);
        return o;
    }

    private Task<Dictionary<string, HomeOfferViewDto>> BuildOffersViewForIdsAsync(
        IReadOnlyList<string> idsInOrder,
        CancellationToken cancellationToken) =>
        RecommendationBatchOfferLoader.BuildOffersViewInOrderAsync(db, offerService, idsInOrder, cancellationToken);

    /// <summary>
    /// Recomputa <see cref="Data.Entities.StoreProductRow.PopularityWeight"/> / <see cref="Data.Entities.StoreServiceRow.PopularityWeight"/>; solo <see cref="AppDbContext"/> para evitar ciclo DI con <see cref="IOfferService"/>.
    /// </summary>
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
    }
}
