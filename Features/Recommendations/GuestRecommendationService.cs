using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Recommendations;

public interface IGuestRecommendationService
{
    Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        CancellationToken cancellationToken = default);
}

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
    private const int IdChunkSize = 500;

    public async Task<RecommendationBatchResponse> GetBatchAsync(
        string guestId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length == 0)
            return RecommendationBatchResponse.Empty(RecommendationService.DefaultBatchSize, RecommendationService.ScoreThreshold);

        var batchSize = Math.Clamp(take, 1, RecommendationService.MaxBatchSize);
        var now = DateTimeOffset.UtcNow;

        var guestEvents = guestStore.GetRecent(gid, max: 250);
        var userEvents = guestEvents
            .Select(e => new InteractionPoint(gid, (e.OfferId ?? "").Trim(), e.EventType, e.At))
            .Where(x => x.OfferId.Length > 0)
            .ToList();

        var guestViewer = new UserAccount
        {
            Id = gid,
            SavedOfferIdsJson = "[]",
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

        var candidates = await LoadCandidatesForOfferIdsAsync(gid, pageIds.ToHashSet(StringComparer.Ordinal), cancellationToken);
        var filtered = pageIds.Where(id => candidates.ContainsKey(id)).ToArray();
        if (filtered.Length == 0)
            return RecommendationBatchResponse.Empty(batchSize, RecommendationService.ScoreThreshold);

        var offers = await BuildOffersJsonForIdsAsync(filtered, cancellationToken);
        await offerEngagement.EnrichOffersJsonAsync(offers, "g:" + gid, cancellationToken);
        var storeBadges = await BuildStoreBadgesJsonAsync(filtered, cancellationToken);
        return new RecommendationBatchResponse(filtered, offers, storeBadges, batchSize, RecommendationService.ScoreThreshold);
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

    private async Task<JsonObject> BuildStoreBadgesJsonAsync(
        IReadOnlyList<string> pageOfferIds,
        CancellationToken cancellationToken)
    {
        if (pageOfferIds.Count == 0)
            return new JsonObject();

        var storeIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pageOfferIds.Count; i += IdChunkSize)
        {
            var slice = pageOfferIds
                .Skip(i)
                .Take(IdChunkSize)
                .Select(id => (id ?? "").Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (slice.Count == 0)
                continue;

            var fromP = await db.StoreProducts.AsNoTracking()
                .Where(p => slice.Contains(p.Id))
                .Select(p => p.StoreId)
                .ToListAsync(cancellationToken);
            foreach (var sid in fromP)
                storeIds.Add(sid);

            var fromS = await db.StoreServices.AsNoTracking()
                .Where(s => slice.Contains(s.Id))
                .Select(s => s.StoreId)
                .ToListAsync(cancellationToken);
            foreach (var sid in fromS)
                storeIds.Add(sid);
        }

        if (storeIds.Count == 0)
            return new JsonObject();

        var rows = await db.Stores.AsNoTracking()
            .Where(s => storeIds.Contains(s.Id))
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
}
