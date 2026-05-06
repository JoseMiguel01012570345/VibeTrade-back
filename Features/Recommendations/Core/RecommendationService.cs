using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Dtos;

namespace VibeTrade.Backend.Features.Recommendations.Core;

public sealed class RecommendationService(
    AppDbContext db,
    IOfferEngagementService offerEngagement,
    IOfferPopularityWeightService popularityWeight,
    RecommendationFeedV2 feedV2) : IRecommendationService
{
    /// <summary>Valor por omisión de <c>take</c> en la API cuando el cliente no lo envía.</summary>
    public const int DefaultBatchSize = 20;
    /// <summary>Prefetch de recomendaciones en bootstrap; la API no limita el <c>take</c> a este valor.</summary>
    public const int DefaultBootstrapTake = 140;
    public const double ScoreThreshold = 0.35d;

    /// <summary>API: el tamaño de lote sigue al <c>take</c> del cliente (valores &lt; 1 se sustituyen por <see cref="DefaultBatchSize" />).</summary>
    public static int NormalizeClientTake(int take) =>
        take < 1 ? DefaultBatchSize : take;

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

        var batchSize = NormalizeClientTake(take);
        var (orderedIds, candidates) = await BuildOrderedFeedAsync(viewer, batchSize, cancellationToken);
        var pageIds = orderedIds.Where(id => candidates.ContainsKey(id)).ToArray();
        if (pageIds.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, ScoreThreshold);

        var offers = await BuildOffersViewForIdsAsync(pageIds, cancellationToken);
        await offerEngagement.EnrichHomeOffersAsync(offers, "u:" + userId, cancellationToken);
        var storeBadges = await BuildStoreBadgesViewAsync(pageIds, candidates, cancellationToken);
        return new RecommendationBatchResponse(pageIds, offers, storeBadges, batchSize, ScoreThreshold);
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
            EventType = ToStorageValue(eventType),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        if (!RecommendationBatchOfferLoader.IsEmergentPublicationId(pid))
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

        var idList = v2Ids is { Count: > 0 }
            ? v2Ids.Select(id => id.Trim()).Where(id => id.Length > 0).ToArray()
            : Array.Empty<string>();

        if (idList.Length == 0)
            return ([], new Dictionary<string, OfferCandidate>(StringComparer.Ordinal));

        var candidates = await RecommendationBatchOfferLoader.LoadOfferCandidatesAsync(
            db, viewer.Id, idList.ToHashSet(StringComparer.Ordinal), cancellationToken);
        return (idList, candidates);
    }

    private Task<Dictionary<string, OfferCandidate>> LoadCandidatesForOfferIdsAsync(
        string viewerUserId,
        IReadOnlyCollection<string> offerIds,
        CancellationToken cancellationToken) =>
        RecommendationBatchOfferLoader.LoadOfferCandidatesAsync(db, viewerUserId, offerIds, cancellationToken);

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

    private async Task<bool> OfferOrEmergentExistsAsync(string offerId, CancellationToken cancellationToken)
    {
        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(offerId))
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
        RecommendationBatchOfferLoader.BuildOffersViewInOrderAsync(db, idsInOrder, cancellationToken);

    private static string ToStorageValue(RecommendationInteractionType eventType) =>
        eventType switch
        {
            RecommendationInteractionType.Click => "click",
            RecommendationInteractionType.Inquiry => "inquiry",
            RecommendationInteractionType.ChatStart => "chat_start",
            _ => "click",
        };
}
