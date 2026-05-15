using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Search.Catalog;
using VibeTrade.Backend.Features.Search.Elasticsearch;
using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Recommendations.Feed;

/// <summary>
/// Recomendaciones V2: grafo de contactos y confianza; semilla 50/20/15/15 (usuario, contacto, random, emergente),
/// puntuación de palabras, Elasticsearch y feed 50/20/15/15 con cadenas de relleno por segmento.
/// Sin cargar el catálogo entero en RAM.
/// </summary>
public sealed class RecommendationFeedV2(
    AppDbContext db,
    IRecommendationElasticsearchQuery elasticsearch)
{
    public const int MaxSeedOffers = 100;
    /// <summary>
    /// Peso de ordenación previa para ofertas de relleno aleatorio (sin señal; el <see cref="BaseSeedWeight" /> mínimo es 0,25).
    /// </summary>
    private const double RandomSeedOrderWeight = 0.25d;
    private const int MaxCombinationsBeforeFallback = 10;

    /// <summary>
    /// Muestra aleatoria acotada de ofertas publicadas (~10% emergentes + productos/servicios al azar).
    /// </summary>
    public Task<List<string>> SampleRandomPublishedOfferIdsAsync(
        string viewerUserId,
        int take,
        HashSet<string> exclude,
        CancellationToken cancellationToken) =>
        SampleRandomOfferIdsAsync(viewerUserId, take, exclude, includeEmergentInSample: true, cancellationToken);
    private const double CommentLikeBoostScale = 0.15d;
    private const double CommentWordWeightMultiplier = 0.25d;
    private const int IdChunkSize = 500;

    public async Task<IReadOnlyList<string>?> TryBuildOrderedOfferIdsAsync(
        string viewerUserId,
        UserAccount viewer,
        IReadOnlyList<string> contactIds,
        List<InteractionPoint> userEvents,
        List<InteractionPoint> contactEvents,
        DateTimeOffset now,
        double scoreThreshold,
        int maxMergedOffers,
        CancellationToken cancellationToken)
    {
        if (!elasticsearch.IsConfigured)
            return null;

        var cap = Math.Max(1, maxMergedOffers);
        var seed = await SelectSeedOffersAsync(
            viewerUserId,
            viewer,
            contactIds,
            userEvents,
            contactEvents,
            now,
            cancellationToken);

        if (seed.Count == 0)
            return null;

        var random = new Random(HashCode.Combine(viewerUserId, DateOnly.FromDateTime(now.UtcDateTime).GetHashCode(), 31));
        var esTake = Math.Min(2000, Math.Max(24, cap * 5));
        var poolTarget = Math.Max(50, cap * 2);

        var userWords = await BuildWordScoresAsync(seed, s => s.Kind is SeedKind.User, cancellationToken);
        var contactWords = await BuildWordScoresAsync(seed, s => s.Kind is SeedKind.Contact, cancellationToken);
        var userNonZero = userWords
            .Where(kv => kv.Value > 0d)
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var contactNonZero = contactWords
            .Where(kv => kv.Value > 0d)
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var userList = userNonZero.Count > 0
            ? await CollectBulkHitsAsync(
                [..userNonZero.Keys], viewerUserId, poolTarget, scoreThreshold, random, esTake, cancellationToken)
            : [];
        var contactList = contactNonZero.Count > 0
            ? await CollectBulkHitsAsync(
                [..contactNonZero.Keys], viewerUserId, poolTarget, scoreThreshold, random, esTake, cancellationToken)
            : [];

        var userQ = RecommendationUtils.ToQueueByScoreOrder(userList);
        var contactQ = RecommendationUtils.ToQueueByScoreOrder(contactList);
        var seedIdSet = seed.Select(s => s.OfferId).ToHashSet(StringComparer.Ordinal);
        var emergentOrdered = new List<string>();
        foreach (var s in seed.Where(x => x.Kind == SeedKind.Emergent))
        {
            if (emergentOrdered.Count < poolTarget)
                emergentOrdered.Add(s.OfferId);
        }

        var moreEmerg = await EmergentRouteOfferRanking.TakeRandomEmergentOfferIdsAsync(
            db,
            viewerUserId,
            Math.Max(0, poolTarget - emergentOrdered.Count) + 32,
            seedIdSet,
            cancellationToken);
        foreach (var id in moreEmerg)
        {
            if (emergentOrdered.Count >= poolTarget)
                break;
            emergentOrdered.Add(id);
        }

        var emergentQ = RecommendationUtils.ToDistinctQueue(emergentOrdered);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = await BuildFeedFiftyTwentyFifteenFifteenAsync(
            viewerUserId, cap, userQ, contactQ, emergentQ, seen, poolTarget, cancellationToken);
        return merged.Count > 0 ? merged : null;
    }

    private async Task<List<string>> BuildFeedFiftyTwentyFifteenFifteenAsync(
        string viewerUserId,
        int cap,
        Queue<string> userQ,
        Queue<string> contactQ,
        Queue<string> emergentQ,
        HashSet<string> seen,
        int poolTarget,
        CancellationToken cancellationToken)
    {
        var (q1, q2, q3, q4) = RecommendationUtils.QuotasFiftyTwentyFifteenFifteen(cap);
        var all = new List<string>(cap);
        var randomQ = new Queue<string>();

        // Incluir publicaciones emergentes en el relleno aleatorio (~10% vía <see cref="SampleRandomOfferIdsAsync"/>);
        // `seen` evita duplicar ids ya tomados de emergentQ / otros segmentos.
        await RefillRandomQueueAsync(
            viewerUserId, seen, randomQ, Math.Max(poolTarget, cap * 4), noEmergentInSample: false, cancellationToken);

        IReadOnlyList<Queue<string>> seg1 = [userQ, contactQ, emergentQ, randomQ];
        IReadOnlyList<Queue<string>> seg2 = [contactQ, emergentQ, randomQ];
        IReadOnlyList<Queue<string>> seg3 = [emergentQ, randomQ];
        IReadOnlyList<Queue<string>> seg4 = [randomQ];

        await AppendSegmentDrainingOrderAsync(
            all, q1, seen, seg1, viewerUserId, randomQ, poolTarget, noEmergentInRandom: false, cancellationToken);
        await AppendSegmentDrainingOrderAsync(
            all, q2, seen, seg2, viewerUserId, randomQ, poolTarget, noEmergentInRandom: false, cancellationToken);
        await AppendSegmentDrainingOrderAsync(
            all, q3, seen, seg3, viewerUserId, randomQ, poolTarget, noEmergentInRandom: false, cancellationToken);
        await AppendSegmentDrainingOrderAsync(
            all, q4, seen, seg4, viewerUserId, randomQ, poolTarget, noEmergentInRandom: false, cancellationToken);
        return all;
    }

    private async Task AppendSegmentDrainingOrderAsync(
        List<string> all,
        int segmentLen,
        HashSet<string> seen,
        IReadOnlyList<Queue<string>> sourceOrder,
        string viewerUserId,
        Queue<string> randomQ,
        int poolTarget,
        bool noEmergentInRandom,
        CancellationToken cancellationToken)
    {
        if (segmentLen <= 0)
            return;
        var target = all.Count + segmentLen;
        var stall = 0;
        while (all.Count < target)
        {
            var countBeforePass = all.Count;
            var randomBeforePass = randomQ.Count;
            foreach (var q in sourceOrder)
            {
                while (all.Count < target)
                {
                    if (!TakeOneNotSeen(q, all, seen))
                        break;
                }
                if (all.Count >= target)
                    return;
            }
            if (all.Count > countBeforePass)
            {
                stall = 0;
                continue;
            }
            await RefillRandomQueueAsync(
                viewerUserId, seen, randomQ, randomQ.Count + poolTarget, noEmergentInRandom, cancellationToken);
            if (randomQ.Count == randomBeforePass)
            {
                stall++;
                if (stall > 12)
                    return;
            }
            else
                stall = 0;
        }
    }

    private static bool TakeOneNotSeen(Queue<string> q, List<string> dest, HashSet<string> seen)
    {
        while (q.Count > 0)
        {
            var x = q.Dequeue();
            if (string.IsNullOrWhiteSpace(x))
                continue;
            if (seen.Add(x))
            {
                dest.Add(x);
                return true;
            }
        }
        return false;
    }

    private async Task RefillRandomQueueAsync(
        string viewerUserId,
        HashSet<string> seen,
        Queue<string> randomQ,
        int atLeast,
        bool noEmergentInSample,
        CancellationToken cancellationToken)
    {
        if (randomQ.Count >= atLeast)
            return;
        var exclude = new HashSet<string>(seen, StringComparer.Ordinal);
        foreach (var id in randomQ.ToArray())
            exclude.Add(id);
        var need = Math.Max(32, atLeast - randomQ.Count);
        var batch = await SampleRandomOfferIdsAsync(
            viewerUserId, need, exclude, includeEmergentInSample: !noEmergentInSample, cancellationToken);
        foreach (var id in batch)
        {
            if (string.IsNullOrWhiteSpace(id) || exclude.Contains(id))
                continue;
            exclude.Add(id);
            randomQ.Enqueue(id);
        }
    }

    private async Task<List<SeedOffer>> SelectSeedOffersAsync(
        string viewerUserId,
        UserAccount viewer,
        IReadOnlyList<string> contactIds,
        List<InteractionPoint> userEvents,
        List<InteractionPoint> contactEvents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var saved = new HashSet<string>(
            viewer.SavedOfferIds
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0),
            StringComparer.Ordinal);

        var userOrdered = await BuildUserOfferOrderedListAsync(userEvents, saved, viewerUserId, cancellationToken);
        var contactOrdered = await BuildContactOfferOrderedListAsync(contactEvents, viewerUserId, cancellationToken);

        var picked = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SeedOffer>(MaxSeedOffers);

        void AddMany(IReadOnlyList<(string Id, double W)> list, SeedKind kind, int cap)
        {
            foreach (var (id, w) in list)
            {
                if (result.Count >= MaxSeedOffers)
                    return;
                if (cap <= 0)
                    return;
                if (!picked.Add(id))
                    continue;
                result.Add(new SeedOffer(id, kind, w));
                cap--;
            }
        }

        const int nUser = 50;
        const int nContact = 20;
        const int nEmergent = 15;

        AddMany(userOrdered, SeedKind.User, nUser);
        AddMany(contactOrdered, SeedKind.Contact, nContact);

        var emergentIds = await EmergentRouteOfferRanking.TakeRandomEmergentOfferIdsAsync(
            db, viewerUserId, nEmergent, picked, cancellationToken);
        foreach (var id in emergentIds)
        {
            if (result.Count >= MaxSeedOffers)
                break;
            if (!picked.Add(id))
                continue;
            result.Add(new SeedOffer(id, SeedKind.Emergent, RandomSeedOrderWeight));
        }

        // Tramo 15 % random + compensación de huecos: catálogo sin mezclar emergentes aquí.
        var needRandom = Math.Max(0, MaxSeedOffers - result.Count);
        if (needRandom > 0)
        {
            var randomIds = await SampleRandomOfferIdsAsync(
                viewerUserId, needRandom, picked, includeEmergentInSample: true, cancellationToken);
            AddMany(randomIds.Select(id => (id, RandomSeedOrderWeight)).ToList(), SeedKind.Random, needRandom);
        }
        return result;
    }

    private async Task<List<(string Id, double W)>> BuildUserOfferOrderedListAsync(
        IReadOnlyList<InteractionPoint> userEvents,
        HashSet<string> saved,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var offerIds = userEvents.Select(e => e.OfferId).Concat(saved).Distinct(StringComparer.Ordinal).ToList();
        var eligible = await FilterEligibleOfferIdsAsync(offerIds, viewerUserId, cancellationToken);
        var byOffer = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var ev in userEvents)
        {
            if (!eligible.Contains(ev.OfferId))
                continue;
            AddWeight(byOffer, ev.OfferId, EventWeight(ev.EventType));
        }

        foreach (var id in saved)
        {
            if (eligible.Contains(id))
                AddWeight(byOffer, id, 1.5d);
        }

        return byOffer
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private async Task<List<(string Id, double W)>> BuildContactOfferOrderedListAsync(
        IReadOnlyList<InteractionPoint> contactEvents,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var ids = contactEvents.Select(e => e.OfferId).Distinct(StringComparer.Ordinal).ToList();
        var eligible = await FilterEligibleOfferIdsAsync(ids, viewerUserId, cancellationToken);
        var trustByOffer = await LoadTrustScoreByOfferIdAsync(
            contactEvents.Where(e => eligible.Contains(e.OfferId)).Select(e => e.OfferId).Distinct(StringComparer.Ordinal).ToList(),
            cancellationToken);

        var byOffer = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var ev in contactEvents)
        {
            if (!eligible.Contains(ev.OfferId))
                continue;
            var w = EventWeight(ev.EventType);
            if (trustByOffer.TryGetValue(ev.OfferId, out var ts))
                w *= TrustNorm(ts);
            AddWeight(byOffer, ev.OfferId, w);
        }

        return byOffer
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private async Task<Dictionary<string, int>> LoadTrustScoreByOfferIdAsync(
        IReadOnlyList<string> offerIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (offerIds.Count == 0)
            return map;

        foreach (var chunk in RecommendationUtils.Chunk(offerIds, IdChunkSize))
        {
            var c = chunk.ToList();
            var fromP = await db.StoreProducts.AsNoTracking()
                .Where(p => c.Contains(p.Id))
                .Select(p => new { p.Id, p.Store.TrustScore })
                .ToListAsync(cancellationToken);
            foreach (var row in fromP)
                map[row.Id] = row.TrustScore;

            var fromS = await db.StoreServices.AsNoTracking()
                .Where(s => c.Contains(s.Id))
                .Select(s => new { s.Id, s.Store.TrustScore })
                .ToListAsync(cancellationToken);
            foreach (var row in fromS)
                map[row.Id] = row.TrustScore;
        }

        return map;
    }

    private Task<List<string>> RandomSeedFromCatalogAsync(
        string viewerUserId,
        int take,
        CancellationToken cancellationToken) =>
        SampleRandomOfferIdsAsync(
            viewerUserId, take, new HashSet<string>(StringComparer.Ordinal), includeEmergentInSample: true, cancellationToken);

    private async Task<List<string>> SampleRandomOfferIdsAsync(
        string viewerUserId,
        int take,
        HashSet<string> exclude,
        bool includeEmergentInSample,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
            return [];

        var want = take;
        var merged = new List<string>(want);
        var seen = new HashSet<string>(exclude, StringComparer.Ordinal);
        void TryAddRange(IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                if (merged.Count >= want)
                    return;
                if (!seen.Add(id))
                    continue;
                merged.Add(id);
            }
        }

        if (includeEmergentInSample)
        {
            // ~10% de la muestra: emergentes (hoja de ruta), elegibles para el viewer.
            var emergentQuota = Math.Min(want, Math.Max(1, (int)Math.Ceiling(want * 0.1d)));
            var fromEmergent = await EmergentRouteOfferRanking.TakeRandomEmergentOfferIdsAsync(
                db,
                viewerUserId,
                emergentQuota,
                seen,
                cancellationToken);
            TryAddRange(fromEmergent);
        }

        var remaining = want - merged.Count;
        if (remaining <= 0)
            return merged.Take(want).ToList();

        var half = (remaining + 1) / 2;
        var fromP = await SampleProductIdsRandomAsync(viewerUserId, half, cancellationToken);
        var fromS = await SampleServiceIdsRandomAsync(viewerUserId, remaining - fromP.Count, cancellationToken);

        TryAddRange(fromP);
        TryAddRange(fromS);

        if (merged.Count < want)
            TryAddRange(await SampleProductIdsRandomAsync(viewerUserId, want * 2, cancellationToken));
        if (merged.Count < want)
            TryAddRange(await SampleServiceIdsRandomAsync(viewerUserId, want * 2, cancellationToken));

        return merged.Take(want).ToList();
    }

    private async Task<List<string>> SampleProductIdsRandomAsync(
        string viewerUserId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
            return [];
        return await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published && p.Store.OwnerUserId != viewerUserId)
            .OrderBy(_ => EF.Functions.Random())
            .Select(p => p.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<string>> SampleServiceIdsRandomAsync(
        string viewerUserId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
            return [];
        return await db.StoreServices.AsNoTracking()
            .Where(s => (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
            .OrderBy(_ => EF.Functions.Random())
            .Select(s => s.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<string>> FilterEligibleOfferIdsAsync(
        IReadOnlyCollection<string> offerIds,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (offerIds.Count == 0)
            return set;

        var idList = offerIds.Distinct(StringComparer.Ordinal).ToList();
        var emergentIds = idList.Where(OfferUtils.IsEmergentPublicationId).ToList();
        var catalogIds = idList.Where(id => !OfferUtils.IsEmergentPublicationId(id)).ToList();

        if (emergentIds.Count > 0)
        {
            var emOk = await db.EmergentOffers.AsNoTracking()
                .Where(e =>
                    emergentIds.Contains(e.Id)
                    && e.RetractedAtUtc == null
                    && e.PublisherUserId != viewerUserId
                    && db.ChatRouteSheets.Any(r =>
                        r.ThreadId == e.ThreadId
                        && r.RouteSheetId == e.RouteSheetId
                        && r.DeletedAtUtc == null
                        && r.PublishedToPlatform))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in emOk)
                set.Add(id);
        }

        foreach (var chunk in RecommendationUtils.Chunk(catalogIds, IdChunkSize))
        {
            var c = chunk.ToList();
            var pIds = await db.StoreProducts.AsNoTracking()
                .Where(p => c.Contains(p.Id) && p.Published && p.Store.OwnerUserId != viewerUserId)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in pIds)
                set.Add(id);

            var sIds = await db.StoreServices.AsNoTracking()
                .Where(s => c.Contains(s.Id) && (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in sIds)
                set.Add(id);
        }

        return set;
    }

    private async Task<HashSet<string>> FilterEligibleOfferIdsFromHitsAsync(
        IReadOnlyList<RecommendationElasticsearchHit> hits,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var ids = hits.Select(h => h.OfferId).Distinct(StringComparer.Ordinal).ToList();
        return await FilterEligibleOfferIdsAsync(ids, viewerUserId, cancellationToken);
    }

    private async Task<Dictionary<string, double>> BuildWordScoresAsync(
        IReadOnlyList<SeedOffer> seed,
        Func<SeedOffer, bool> includeOffer,
        CancellationToken cancellationToken)
    {
        var filtered = seed.Where(includeOffer).ToList();
        if (filtered.Count == 0)
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var ids = filtered.Select(s => s.OfferId).Distinct(StringComparer.Ordinal).ToList();
        var seedWeightByOffer = filtered.GroupBy(s => s.OfferId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => BaseSeedWeight(x)), StringComparer.Ordinal);

        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var commentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in products)
            CollectQaIds(p.OfferQa, commentIds);
        foreach (var s in services)
            CollectQaIds(s.OfferQa, commentIds);

        var likeCounts = await LoadCommentLikeCountsAsync(ids, commentIds, cancellationToken);

        var offerScores = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        var commentScores = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in products)
        {
            if (!seedWeightByOffer.TryGetValue(p.Id, out var sw))
                continue;
            var mainText = RecommendationUtils.ConcatOfferMainTextProduct(p);
            AddTokens(offerScores, mainText, sw * 1.0d);
            AddCommentTokens(p.Id, p.OfferQa, sw, likeCounts, commentScores);
        }

        foreach (var s in services)
        {
            if (!seedWeightByOffer.TryGetValue(s.Id, out var sw))
                continue;
            var mainText = RecommendationUtils.ConcatOfferMainTextService(s);
            AddTokens(offerScores, mainText, sw * 1.0d);
            AddCommentTokens(s.Id, s.OfferQa, sw, likeCounts, commentScores);
        }

        var final = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var keys = offerScores.Keys.Union(commentScores.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var k in keys)
        {
            offerScores.TryGetValue(k, out var oList);
            commentScores.TryGetValue(k, out var cList);
            var avgO = oList is { Count: > 0 } ? oList.Average() : double.NaN;
            var avgC = cList is { Count: > 0 } ? cList.Average() : double.NaN;
            double v;
            if (!double.IsNaN(avgO) && !double.IsNaN(avgC))
                v = Math.Max(avgO, avgC);
            else if (!double.IsNaN(avgO))
                v = avgO;
            else
                v = avgC;
            if (!double.IsNaN(v) && v > 0d)
                final[k] = v;
        }

        return final;
    }

    private static double BaseSeedWeight(SeedOffer s) =>
        s.Kind switch
        {
            SeedKind.User => Math.Max(0.4d, s.Weight),
            SeedKind.Contact => Math.Max(0.35d, s.Weight),
            SeedKind.Emergent => Math.Max(0.25d, s.Weight),
            _ => Math.Max(0.25d, s.Weight),
        };

    private void AddCommentTokens(
        string offerId,
        IReadOnlyList<OfferQaComment> items,
        double seedWeight,
        IReadOnlyDictionary<string, int> likeCounts,
        Dictionary<string, List<double>> commentScores)
    {
        foreach (var o in items)
        {
            var cid = o.Id.Trim();
            var text = new List<string>();
            if (!string.IsNullOrWhiteSpace(o.Text))
                text.Add(o.Text.Trim());
            if (!string.IsNullOrWhiteSpace(o.Question))
                text.Add(o.Question.Trim());
            if (!string.IsNullOrWhiteSpace(o.Answer))
                text.Add(o.Answer.Trim());

            if (text.Count == 0)
                continue;
            var merged = string.Join(' ', text);
            var likes = cid.Length > 0 && likeCounts.TryGetValue($"{offerId}::{cid}", out var n) ? n : 0;
            var likeBoost = 1d + CommentLikeBoostScale * Math.Log(1 + likes);
            var w = seedWeight * CommentWordWeightMultiplier * likeBoost;
            AddTokens(commentScores, merged, w);
        }
    }

    private async Task<Dictionary<string, int>> LoadCommentLikeCountsAsync(
        IReadOnlyList<string> offerIds,
        IReadOnlyCollection<string> commentIds,
        CancellationToken cancellationToken)
    {
        if (offerIds.Count == 0 || commentIds.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        var idList = commentIds.ToList();
        var rows = await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => offerIds.Contains(x.OfferId) && idList.Contains(x.QaCommentId))
            .GroupBy(x => new { x.OfferId, x.QaCommentId })
            .Select(g => new { g.Key.OfferId, g.Key.QaCommentId, C = g.Count() })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
            map[r.OfferId + "::" + r.QaCommentId] = r.C;
        return map;
    }

    private static void CollectQaIds(IReadOnlyList<OfferQaComment> items, HashSet<string> target)
    {
        foreach (var c in items)
        {
            var id = c.Id.Trim();
            if (id.Length > 0)
                target.Add(id);
        }
    }

    private async Task<List<(string OfferId, double Score)>> CollectBulkHitsAsync(
        IReadOnlyList<string> bulkWords,
        string viewerUserId,
        int targetDistinct,
        double relativeThreshold,
        Random random,
        int elasticsearchTake,
        CancellationToken cancellationToken)
    {
        if (bulkWords.Count == 0 || targetDistinct <= 0)
            return [];

        var pool = new Dictionary<string, double>(StringComparer.Ordinal);
        var allHits = new List<RecommendationElasticsearchHit>();

        async Task RunQueryAsync(string query, bool applyThreshold)
        {
            var raw = await elasticsearch.SearchOffersAsync(query, elasticsearchTake, cancellationToken);
            if (raw.Count == 0)
                return;
            var eligible = await FilterEligibleOfferIdsFromHitsAsync(raw, viewerUserId, cancellationToken);
            var hits = raw.Where(h => eligible.Contains(h.OfferId)).ToList();
            if (hits.Count == 0)
                return;
            allHits.AddRange(hits);
            var maxS = hits.Max(h => h.Score);
            if (maxS <= 0d)
                maxS = 1d;
            foreach (var h in hits)
            {
                if (applyThreshold && h.Score < relativeThreshold * maxS)
                    continue;
                if (!pool.TryGetValue(h.OfferId, out var ex) || h.Score > ex)
                    pool[h.OfferId] = h.Score;
            }
        }

        for (var attempt = 0; attempt < MaxCombinationsBeforeFallback; attempt++)
        {
            var q = BuildRandomCombo(bulkWords, random);
            await RunQueryAsync(q, applyThreshold: true);
            if (pool.Count >= targetDistinct)
                break;
        }

        if (pool.Count < targetDistinct && allHits.Count > 0)
        {
            var maxS = allHits.Max(h => h.Score);
            if (maxS <= 0d)
                maxS = 1d;
            foreach (var h in allHits)
            {
                if (h.Score < relativeThreshold * maxS)
                    continue;
                if (!pool.TryGetValue(h.OfferId, out var ex) || h.Score > ex)
                    pool[h.OfferId] = h.Score;
            }
        }

        if (pool.Count < targetDistinct)
        {
            for (var extra = 0; extra < 25 && pool.Count < targetDistinct; extra++)
            {
                var q = BuildRandomCombo(bulkWords, random);
                await RunQueryAsync(q, applyThreshold: false);
            }
        }

        return pool
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .Take(Math.Max(targetDistinct * 2, targetDistinct))
            .ToList();
    }

    private static string BuildRandomCombo(IReadOnlyList<string> words, Random random)
    {
        var n = words.Count;
        if (n == 0)
            return "";
        if (n == 1)
            return words[0];
        var pick = n == 2 ? 2 : random.Next(2, Math.Min(4, n + 1));
        pick = Math.Clamp(pick, 2, n);
        if (n <= 2)
            return string.Join(' ', words.Take(n));

        var idx = Enumerable.Range(0, n).OrderBy(_ => random.Next()).Take(Math.Min(pick, n)).OrderBy(i => i).ToArray();
        var parts = idx.Select(i => words[i]).ToArray();
        return string.Join(' ', parts);
    }

    private static void AddTokens(Dictionary<string, List<double>> bag, string text, double weight)
    {
        if (weight <= 0d || string.IsNullOrWhiteSpace(text))
            return;
        foreach (var tok in Tokenize(text))
        {
            if (!bag.TryGetValue(tok, out var list))
            {
                list = [];
                bag[tok] = list;
            }

            list.Add(weight);
        }
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var folded = StoreSearchTextNormalize.FoldForMatch(text);
        foreach (var part in folded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = part.Trim().ToLowerInvariant();
            if (t.Length < 2)
                continue;
            if (t.Length > 48)
                continue;
            yield return t;
        }
    }

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

    private static double TrustNorm(int trustScore) =>
        trustScore switch
        {
            < 0 => 0d,
            > 100 => 1d,
            _ => trustScore / 100d,
        };
}
