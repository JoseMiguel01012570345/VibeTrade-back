using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Recommendations.Guest;

/// <summary>
/// Recomendaciones para invitado: mismo pipeline <see cref="RecommendationFeedV2"/> que usuarios autenticados
/// (semilla desde interacciones en <see cref="IGuestInteractionStore"/> + Elasticsearch).
/// Si ES/V2 no devuelve resultados, se usa una muestra aleatoria acotada (incl. emergentes).
/// </summary>
public sealed class GuestRecommendationService(
    AppDbContext db,
    IGuestInteractionStore guestStore,
    IOfferEngagementService offerEngagement,
    RecommendationFeedV2 feedV2)
    : IGuestRecommendationService
{
    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length == 0)
            return RecommendationBatchResponse.Empty(RecommendationService.DefaultBatchSize, RecommendationService.ScoreThreshold);

        var batchSize = RecommendationService.NormalizeClientTake(take);
        var now = DateTimeOffset.UtcNow;

        var guestEvents = guestStore.GetRecent(gid, max: 250);
        var userEvents = guestEvents
            .Select(e => new InteractionPoint(gid, (e.OfferId ?? "").Trim(), e.EventType, e.At))
            .Where(x => x.OfferId.Length > 0)
            .ToList();

        var guestViewer = new UserAccount
        {
            Id = gid,
            SavedOfferIds = new List<string>(),
        };

        var v2Ids = await feedV2.TryBuildOrderedOfferIdsAsync(
            gid,
            guestViewer,
            contactIds: [],
            userEvents,
            contactEvents: [],
            now,
            RecommendationService.ScoreThreshold,
            batchSize,
            cancellationToken);

        string[] pageIds;
        if (v2Ids is { Count: > 0 })
        {
            pageIds = v2Ids
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .ToArray();
        }
        else
        {
            var randomIds = await feedV2.SampleRandomPublishedOfferIdsAsync(
                gid,
                batchSize,
                new HashSet<string>(StringComparer.Ordinal),
                cancellationToken);
            pageIds = randomIds
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (pageIds.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, RecommendationService.ScoreThreshold);

        var candidates = await RecommendationBatchOfferLoader.LoadOfferCandidatesAsync(
            db, gid, pageIds.ToHashSet(StringComparer.Ordinal), cancellationToken);
        var filtered = pageIds.Where(id => candidates.ContainsKey(id)).ToArray();
        if (filtered.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, RecommendationService.ScoreThreshold);

        var offers = await RecommendationBatchOfferLoader.BuildOffersViewInOrderAsync(db, filtered, cancellationToken);
        await offerEngagement.EnrichHomeOffersAsync(offers, "g:" + gid, cancellationToken);
        var storeBadges = await BuildStoreBadgesFromCandidatesAsync(filtered, candidates, cancellationToken);
        return new RecommendationBatchResponse(filtered, offers, storeBadges, batchSize, RecommendationService.ScoreThreshold);
    }

    private async Task<Dictionary<string, StoreProfileWorkspaceData>> BuildStoreBadgesFromCandidatesAsync(
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
}
