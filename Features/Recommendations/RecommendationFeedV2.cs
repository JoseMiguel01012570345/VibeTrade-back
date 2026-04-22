using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Search;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Recomendaciones V2: palabras ponderadas (oferta vs comentarios + likes), bulks,
/// consultas Elasticsearch y semilla de hasta 100 ofertas (70/20/10). Sin cargar el catálogo completo en RAM.
/// El merge final respeta el tope pedido (típicamente el <c>take</c> del lote).
/// </summary>
public sealed class RecommendationFeedV2(
    AppDbContext db,
    IRecommendationElasticsearchQuery elasticsearch)
{
    public const int MaxSeedOffers = 100;
    private const int MaxCombinationsBeforeFallback = 10;
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

        var wordScores = await BuildWordScoresAsync(seed, cancellationToken);
        var nonZero = wordScores.Where(kv => kv.Value > 0d).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (nonZero.Count == 0)
            return null;

        var (bulk1, bulk2, bulk3) = PartitionIntoBulks(nonZero);
        if (bulk1.Count + bulk2.Count + bulk3.Count == 0)
            return null;

        var random = new Random(HashCode.Combine(viewerUserId, DateOnly.FromDateTime(now.UtcDateTime).GetHashCode(), 31));

        var cap = Math.Clamp(maxMergedOffers, 1, RecommendationService.MaxBatchSize);
        var q1 = Math.Max(1, (int)Math.Round(0.7d * cap));
        var q2 = Math.Max(1, (int)Math.Round(0.2d * cap));
        int q3;
        if (q1 + q2 >= cap)
        {
            q1 = Math.Max(1, cap - 1);
            q2 = cap - q1;
            q3 = 0;
        }
        else
            q3 = cap - q1 - q2;

        var esTake = Math.Min(120, Math.Max(24, cap * 5));

        var hits1 = await CollectBulkHitsAsync(bulk1, viewerUserId, q1, scoreThreshold, random, esTake, cancellationToken);
        var hits2 = await CollectBulkHitsAsync(bulk2, viewerUserId, q2, scoreThreshold, random, esTake, cancellationToken);
        var hits3 = await CollectBulkHitsAsync(bulk3, viewerUserId, q3, scoreThreshold, random, esTake, cancellationToken);

        var merged = MergeRoundRobin(hits1, hits2, hits3, cap);
        return merged.Count > 0 ? merged : null;
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
        var saved = ParseSavedOfferIds(viewer.SavedOfferIdsJson).ToHashSet(StringComparer.Ordinal);
        var hasSignals =
            userEvents.Count > 0
            || saved.Count > 0
            || contactIds.Count > 0;

        if (!hasSignals)
        {
            var ids = await RandomSeedFromCatalogAsync(viewerUserId, MaxSeedOffers, cancellationToken);
            return ids.Select(id => new SeedOffer(id, SeedKind.Random, 0.35d)).ToList();
        }

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

        var nUser = (int)Math.Round(0.7d * MaxSeedOffers);
        var nContact = (int)Math.Round(0.2d * MaxSeedOffers);
        var nRand = MaxSeedOffers - nUser - nContact;

        AddMany(userOrdered, SeedKind.User, nUser);
        AddMany(contactOrdered, SeedKind.Contact, nContact);

        var needRandom = Math.Max(0, nRand + (nUser + nContact - result.Count));
        var randomIds = await SampleRandomOfferIdsAsync(viewerUserId, needRandom, picked, cancellationToken);
        AddMany(randomIds.Select(id => (id, 0.35d)).ToList(), SeedKind.Random, needRandom);
    
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

        foreach (var chunk in Chunk(offerIds, IdChunkSize))
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
        SampleRandomOfferIdsAsync(viewerUserId, take, new HashSet<string>(StringComparer.Ordinal), cancellationToken);

    private async Task<List<string>> SampleRandomOfferIdsAsync(
        string viewerUserId,
        int take,
        HashSet<string> exclude,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
            return [];

        var want = take;
        var half = (want + 1) / 2;
        var fromP = await SampleProductIdsRandomAsync(viewerUserId, half, cancellationToken);
        var fromS = await SampleServiceIdsRandomAsync(viewerUserId, want - fromP.Count, cancellationToken);
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

        foreach (var chunk in Chunk(offerIds.Distinct(StringComparer.Ordinal), IdChunkSize))
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
        CancellationToken cancellationToken)
    {
        var ids = seed.Select(s => s.OfferId).Distinct(StringComparer.Ordinal).ToList();
        var seedWeightByOffer = seed.GroupBy(s => s.OfferId, StringComparer.Ordinal)
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
            var mainText = ConcatOfferMainTextProduct(p);
            AddTokens(offerScores, mainText, sw * 1.0d);
            AddCommentTokens(p.Id, p.OfferQa, sw, likeCounts, commentScores);
        }

        foreach (var s in services)
        {
            if (!seedWeightByOffer.TryGetValue(s.Id, out var sw))
                continue;
            var mainText = ConcatOfferMainTextService(s);
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

    private static (List<string> B1, List<string> B2, List<string> B3) PartitionIntoBulks(
        IReadOnlyDictionary<string, double> wordScores)
    {
        var list = wordScores
            .Where(kv => kv.Key.Length > 0)
            .Select(kv => (Word: kv.Key, Score: kv.Value))
            .OrderByDescending(x => x.Score)
            .ToList();

        if (list.Count == 0)
            return ([], [], []);

        var min = list[^1].Score;
        var max = list[0].Score;
        var bulkScore = (max - min) / 3d;

        var b1 = new List<string>();
        var b2 = new List<string>();
        var b3 = new List<string>();

        if (bulkScore <= 1e-9d)
        {
            foreach (var x in list)
                b1.Add(x.Word);
            return (b1, b2, b3);
        }

        foreach (var x in list)
        {
            var t = max - x.Score;
            if (t <= bulkScore)
                b1.Add(x.Word);
            else if (t <= 2d * bulkScore)
                b2.Add(x.Word);
            else
                b3.Add(x.Word);
        }

        return (b1, b2, b3);
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

    private static List<string> MergeRoundRobin(
        IReadOnlyList<(string OfferId, double Score)> a,
        IReadOnlyList<(string OfferId, double Score)> b,
        IReadOnlyList<(string OfferId, double Score)> c,
        int maxLen)
    {
        var qa = new Queue<(string Id, double S)>(a.Select(x => (x.OfferId, x.Score)));
        var qb = new Queue<(string Id, double S)>(b.Select(x => (x.OfferId, x.Score)));
        var qc = new Queue<(string Id, double S)>(c.Select(x => (x.OfferId, x.Score)));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outList = new List<string>(maxLen);

        void TryTake(Queue<(string Id, double S)> q)
        {
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                if (seen.Add(x.Id))
                {
                    outList.Add(x.Id);
                    return;
                }
            }
        }

        while (outList.Count < maxLen && (qa.Count > 0 || qb.Count > 0 || qc.Count > 0))
        {
            for (var i = 0; i < 7 && outList.Count < maxLen; i++)
                TryTake(qa);
            for (var i = 0; i < 2 && outList.Count < maxLen; i++)
                TryTake(qb);
            if (outList.Count < maxLen)
                TryTake(qc);
        }

        return outList;
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

    private static string ConcatOfferMainTextProduct(StoreProductRow p)
    {
        var parts = new[]
        {
            p.Name, p.Category, p.Model, p.ShortDescription, p.MainBenefit, p.TechnicalSpecs,
            p.Condition, p.Price, p.Availability, p.WarrantyReturn, p.ContentIncluded,
            p.UsageConditions, p.CustomFieldsJson,
        };
        return string.Join(' ', parts.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string ConcatOfferMainTextService(StoreServiceRow s)
    {
        var parts = new[]
        {
            s.Category, s.TipoServicio, s.Descripcion, s.Incluye, s.NoIncluye, s.Entregables,
            s.PropIntelectual, s.RiesgosJson, s.DependenciasJson, s.GarantiasJson, s.CustomFieldsJson,
        };
        return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
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

    private static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        using var e = source.GetEnumerator();
        while (e.MoveNext())
        {
            var chunk = new List<T>(size) { e.Current };
            for (var i = 1; i < size && e.MoveNext(); i++)
                chunk.Add(e.Current);
            yield return chunk;
        }
    }

    private sealed record SeedOffer(string OfferId, SeedKind Kind, double Weight);

    private enum SeedKind
    {
        User,
        Contact,
        Random,
    }
}
