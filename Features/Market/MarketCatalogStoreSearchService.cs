using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Search;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketCatalogStoreSearchService(
    IElasticsearchStoreSearchQuery storeSearchQuery,
    AppDbContext db) : IMarketCatalogStoreSearchService
{
    private sealed record StoreSearchContext(
        int Take,
        int Skip,
        string? NameParam,
        string? CategoryParam,
        IReadOnlyList<string> Kinds,
        int? TrustMin,
        string NameQuery,
        string CategoryQuery,
        bool HasDistanceFilter,
        double? Lat,
        double? Lng,
        double? Km);

    public async Task<StoreSearchResponse> SearchCatalogAsync(
        string? name,
        string? category,
        string? kinds,
        int? trustMin,
        double? lat,
        double? lng,
        double? km,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var ctx = ParseStoreSearchContext(name, category, kinds, trustMin, lat, lng, km, limit, offset);
        if (!storeSearchQuery.IsConfigured)
            return new StoreSearchResponse([], false, ctx.Skip, ctx.Take);

        var esResponse = await SearchStoresViaElasticsearchFillPageAsync(ctx, cancellationToken);
        return esResponse ?? new StoreSearchResponse([], false, ctx.Skip, ctx.Take);
    }

    public async Task<StoreAutocompleteResponse> AutocompleteCatalogAsync(
        string? q,
        string? category,
        string? kinds,
        int? limit,
        CancellationToken cancellationToken)
    {
        var query = (q ?? "").Trim();
        if (query.Length < 2)
            return new StoreAutocompleteResponse(Array.Empty<string>());
        if (query.Length > 80)
            query = query[..80];

        var take = Math.Clamp(limit ?? 10, 1, 25);

        var kindList = ParseKindsParam(kinds);
        var wantStores = kindList.Contains(CatalogSearchKinds.Store, StringComparer.Ordinal);
        var wantProducts = kindList.Contains(CatalogSearchKinds.Product, StringComparer.Ordinal);
        var wantServices = kindList.Contains(CatalogSearchKinds.Service, StringComparer.Ordinal);

        var catQ = (category ?? "").Trim();
        var catParts = catQ.Length == 0
            ? Array.Empty<string>()
            : catQ.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
        var catLowerParts = catParts.Length == 0
            ? Array.Empty<string>()
            : catParts.Select(x => x.ToLowerInvariant()).ToArray();

        var patterns = BuildIlikePatterns(query);

        const int MaxCandidatesPerKind = 120;

        var storeCandidates = new List<string>();
        if (wantStores)
        {
            var storeRows = new List<(string? Name, string? CategoriesJson)>(MaxCandidatesPerKind);
            foreach (var pat in patterns)
            {
                if (storeRows.Count >= MaxCandidatesPerKind) break;
                var qStores = db.Stores.AsNoTracking()
                    .Where(s =>
                        (s.Name != null && EF.Functions.ILike(s.Name, pat)) ||
                        (s.NormalizedName != null && EF.Functions.ILike(s.NormalizedName, pat)));

                var slice = await qStores
                    .OrderByDescending(s => s.TrustScore)
                    .Select(s => new { s.Name, s.CategoriesJson })
                    .Take(MaxCandidatesPerKind)
                    .ToListAsync(cancellationToken);

                foreach (var r in slice)
                    storeRows.Add((r.Name, r.CategoriesJson));
            }

            if (catLowerParts.Length == 0)
                storeCandidates = storeRows.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
            else
                storeCandidates = storeRows
                    .Where(x =>
                    {
                        var cj = (x.CategoriesJson ?? "").ToLowerInvariant();
                        return cj.Length > 0 && catLowerParts.Any(c => cj.Contains(c, StringComparison.Ordinal));
                    })
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList();
        }

        var productCandidates = new List<string>();
        if (wantProducts)
        {
            var outNames = new List<string>(MaxCandidatesPerKind);
            foreach (var pat in patterns)
            {
                if (outNames.Count >= MaxCandidatesPerKind) break;
                var qProducts = db.StoreProducts.AsNoTracking()
                    .Where(p => p.Published &&
                                (EF.Functions.ILike(p.Name, pat) ||
                                 (p.Model != null && EF.Functions.ILike(p.Model, pat))));
                if (catParts.Length > 0)
                    qProducts = qProducts.Where(p => catLowerParts.Contains((p.Category ?? "").ToLower()));

                var slice = await qProducts
                    .OrderBy(p => p.Name)
                    .Select(p => p.Name)
                    .Take(MaxCandidatesPerKind)
                    .ToListAsync(cancellationToken);

                outNames.AddRange(slice);
            }

            productCandidates = outNames;
        }

        var serviceCandidates = new List<string>();
        if (wantServices)
        {
            var outNames = new List<string>(MaxCandidatesPerKind);
            foreach (var pat in patterns)
            {
                if (outNames.Count >= MaxCandidatesPerKind) break;
                var qServices = db.StoreServices.AsNoTracking()
                    .Where(s => (s.Published == null || s.Published == true) &&
                                (EF.Functions.ILike(s.TipoServicio, pat) ||
                                 (s.Category != null && EF.Functions.ILike(s.Category, pat))));
                if (catParts.Length > 0)
                    qServices = qServices.Where(s => catLowerParts.Contains((s.Category ?? "").ToLower()));

                var slice = await qServices
                    .OrderBy(s => s.TipoServicio)
                    .Select(s => s.TipoServicio)
                    .Take(MaxCandidatesPerKind)
                    .ToListAsync(cancellationToken);

                outNames.AddRange(slice);
            }

            serviceCandidates = outNames;
        }

        var qFold = Fold(query);

        var merged = Clean(storeCandidates)
            .Concat(Clean(productCandidates))
            .Concat(Clean(serviceCandidates))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => (s, score: ScoreCandidate(qFold, s)))
            .Where(x => x.score > int.MinValue / 2)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.s.Length)
            .Take(take)
            .Select(x => x.s)
            .ToList();

        return new StoreAutocompleteResponse(merged);
    }

    private static IReadOnlyList<string> BuildIlikePatterns(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length == 0) return Array.Empty<string>();
        if (q.Length > 80) q = q[..80];

        var patterns = new List<string>(12) { $"%{q}%" };

        if (q.Length is >= 3 and <= 10)
        {
            for (var i = 0; i < q.Length && patterns.Count < 10; i++)
            {
                var one = q.ToCharArray();
                one[i] = '_';
                patterns.Add($"%{new string(one)}%");
            }
        }

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Fold(string s) => (s ?? "").Trim().ToLowerInvariant();

    private static int EditDistanceLevenshtein(string a, string b, int maxDistance)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        if (Math.Abs(a.Length - b.Length) > maxDistance) return maxDistance + 1;

        if (a.Length > b.Length)
        {
            var tmp = a;
            a = b;
            b = tmp;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var bestRow = curr[0];
            var ca = a[i - 1];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = ca == b[j - 1] ? 0 : 1;
                var ins = curr[j - 1] + 1;
                var del = prev[j] + 1;
                var sub = prev[j - 1] + cost;
                var v = ins < del ? ins : del;
                if (sub < v) v = sub;
                curr[j] = v;
                if (v < bestRow) bestRow = v;
            }

            if (bestRow > maxDistance) return maxDistance + 1;

            var t = prev;
            prev = curr;
            curr = t;
        }

        return prev[b.Length];
    }

    private static int ScoreCandidate(string qFold, string cand)
    {
        var c = (cand ?? "").Trim();
        if (c.Length == 0) return int.MinValue;
        var cf = Fold(c);
        if (cf == qFold) return 10_000;
        if (cf.StartsWith(qFold, StringComparison.Ordinal)) return 9_000 - (cf.Length - qFold.Length);
        if (cf.Contains(qFold, StringComparison.Ordinal)) return 7_500 - Math.Max(0, cf.Length - qFold.Length);

        var maxD = qFold.Length <= 4 ? 1 : qFold.Length <= 8 ? 2 : 3;
        var d = EditDistanceLevenshtein(qFold, cf, maxD);
        if (d <= maxD) return 6_000 - d * 500 - Math.Abs(cf.Length - qFold.Length) * 10;
        return int.MinValue;
    }

    private static IEnumerable<string> Clean(IEnumerable<string> xs) =>
        xs.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0);

    private static StoreSearchContext ParseStoreSearchContext(
        string? name,
        string? category,
        string? kinds,
        int? trustMin,
        double? lat,
        double? lng,
        double? km,
        int? limit,
        int? offset)
    {
        var take = Math.Clamp(limit ?? 40, 1, 200);
        var skip = Math.Max(0, offset ?? 0);
        var nameQ = (name ?? "").Trim();
        var catQ = (category ?? "").Trim();
        var kindList = ParseKindsParam(kinds);
        var tm = trustMin;
        if (tm.HasValue)
        {
            if (tm.Value < 0) tm = 0;
            if (tm.Value > 100) tm = 100;
        }

        var hasDistanceFilter = lat.HasValue && lng.HasValue && km.HasValue && km.Value > 0;
        if (hasDistanceFilter)
        {
            if (!double.IsFinite(lat!.Value) || lat.Value is < -90 or > 90) hasDistanceFilter = false;
            if (!double.IsFinite(lng!.Value) || lng.Value is < -180 or > 180) hasDistanceFilter = false;
            if (!double.IsFinite(km!.Value) || km.Value is <= 0 or > 25_000) hasDistanceFilter = false;
        }

        return new StoreSearchContext(take, skip, name, category, kindList, tm, nameQ, catQ, hasDistanceFilter, lat, lng, km);
    }

    private static IReadOnlyList<string> ParseKindsParam(string? kinds)
    {
        if (string.IsNullOrWhiteSpace(kinds))
            return new[] { CatalogSearchKinds.Store, CatalogSearchKinds.Product, CatalogSearchKinds.Service };

        var parts = kinds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var ok = new List<string>(3);
        foreach (var p in parts)
        {
            if (p == CatalogSearchKinds.Store || p == CatalogSearchKinds.Product || p == CatalogSearchKinds.Service)
                ok.Add(p);
        }

        return ok.Count == 0
            ? Array.Empty<string>()
            : ok;
    }

    private async Task<StoreSearchResponse?> SearchStoresViaElasticsearchFillPageAsync(
        StoreSearchContext ctx,
        CancellationToken cancellationToken)
    {
        var want = ctx.Take;
        var skipCursor = ctx.Skip;
        var chunk = ctx.Take;

        ElasticsearchStoreSearchResult? first = null;
        var items = new List<CatalogSearchItem>(want);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        while (items.Count < want)
        {
            var es = await storeSearchQuery.SearchAsync(
                ctx.NameQuery,
                ctx.CategoryQuery,
                ctx.Kinds,
                ctx.TrustMin,
                ctx.HasDistanceFilter,
                ctx.Lat ?? 0,
                ctx.Lng ?? 0,
                ctx.Km ?? 0,
                skipCursor,
                chunk,
                cancellationToken);
            if (es is null)
                return null;
            if (es.Hits.Count == 0)
                break;

            first ??= es;

            var batchItems = await BuildCatalogSearchItemsFromElasticsearchHitsAsync(es.Hits, ctx, cancellationToken);
            foreach (var it in batchItems)
            {
                var key = it.Kind == CatalogSearchKinds.Store
                    ? $"store:{it.Store["id"]}"
                    : it.Offer is null
                        ? $"x:{it.Store["id"]}"
                        : $"{it.Kind}:{it.Offer["id"]}";

                if (!seenKeys.Add(key))
                    continue;

                items.Add(it);
                if (items.Count >= want)
                    break;
            }

            skipCursor += es.Hits.Count;
            if (first.TotalCount > 0 && skipCursor >= first.TotalCount)
                break;
        }

        var hasMore = items.Count == want;
        return new StoreSearchResponse(items, hasMore, ctx.Skip, ctx.Take);
    }

    private async Task<List<CatalogSearchItem>> BuildCatalogSearchItemsFromElasticsearchHitsAsync(
        IReadOnlyList<ElasticsearchStoreSearchHit> hits,
        StoreSearchContext ctx,
        CancellationToken cancellationToken)
    {
        if (hits.Count == 0)
            return new List<CatalogSearchItem>();

        var storeIds = hits.Select(h => h.StoreId).Distinct().ToList();
        var rows = await db.Stores.AsNoTracking()
            .Where(s => storeIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var productIds = hits
            .Where(h => h.Kind == CatalogSearchKinds.Product && !string.IsNullOrEmpty(h.OfferId))
            .Select(h => h.OfferId!)
            .Distinct()
            .ToList();
        var serviceIds = hits
            .Where(h => h.Kind == CatalogSearchKinds.Service && !string.IsNullOrEmpty(h.OfferId))
            .Select(h => h.OfferId!)
            .Distinct()
            .ToList();

        var (publishedProductCountByStore, publishedServiceCountByStore, productsById, servicesById) =
            await LoadPublishedCountsForStoreIdsAsync(storeIds, productIds, serviceIds, cancellationToken);

        var items = new List<CatalogSearchItem>(hits.Count);
        foreach (var hit in hits)
        {
            if (!rows.TryGetValue(hit.StoreId, out var row))
                continue;
            publishedProductCountByStore.TryGetValue(row.Id, out var pp);
            publishedServiceCountByStore.TryGetValue(row.Id, out var ps);
            var storeJson = BuildStoreBadgeJson(row);

            if (ctx.TrustMin is not null && row.TrustScore < ctx.TrustMin.Value)
                continue;

            if (hit.Kind == CatalogSearchKinds.Store)
            {
                items.Add(new CatalogSearchItem(
                    CatalogSearchKinds.Store,
                    storeJson,
                    null,
                    pp,
                    ps,
                    hit.DistanceKm));
                continue;
            }

            if (hit.Kind == CatalogSearchKinds.Product && hit.OfferId is { } pid
                                                     && productsById.TryGetValue(pid, out var pr))
            {
                items.Add(new CatalogSearchItem(
                    CatalogSearchKinds.Product,
                    storeJson,
                    BuildProductOfferJson(pr),
                    pp,
                    ps,
                    hit.DistanceKm));
                continue;
            }

            if (hit.Kind == CatalogSearchKinds.Service && hit.OfferId is { } sid
                                                      && servicesById.TryGetValue(sid, out var sv))
            {
                items.Add(new CatalogSearchItem(
                    CatalogSearchKinds.Service,
                    storeJson,
                    BuildServiceOfferJson(sv),
                    pp,
                    ps,
                    hit.DistanceKm));
            }
        }

        return items;
    }

    private async Task<(
        Dictionary<string, long> PublishedProductCountByStore,
        Dictionary<string, long> PublishedServiceCountByStore,
        Dictionary<string, StoreProductRow> ProductsById,
        Dictionary<string, StoreServiceRow> ServicesById)> LoadPublishedCountsForStoreIdsAsync(
        IReadOnlyCollection<string> storeIds,
        IReadOnlyCollection<string> productIds,
        IReadOnlyCollection<string> serviceIds,
        CancellationToken cancellationToken)
    {
        var productIdSet = productIds.Count == 0 ? null : productIds.ToHashSet(StringComparer.Ordinal);
        var serviceIdSet = serviceIds.Count == 0 ? null : serviceIds.ToHashSet(StringComparer.Ordinal);

        var publishedProducts = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published && storeIds.Contains(p.StoreId))
            .ToListAsync(cancellationToken);

        var publishedServices = await db.StoreServices.AsNoTracking()
            .Where(s => (s.Published == null || s.Published == true) && storeIds.Contains(s.StoreId))
            .ToListAsync(cancellationToken);

        var publishedProductCountByStore = publishedProducts
            .GroupBy(p => p.StoreId)
            .ToDictionary(g => g.Key, g => (long)g.Count(), StringComparer.Ordinal);

        var publishedServiceCountByStore = publishedServices
            .GroupBy(s => s.StoreId)
            .ToDictionary(g => g.Key, g => (long)g.Count(), StringComparer.Ordinal);

        var productsById = productIdSet is null
            ? new Dictionary<string, StoreProductRow>(StringComparer.Ordinal)
            : publishedProducts
                .Where(p => productIdSet.Contains(p.Id))
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);

        var servicesById = serviceIdSet is null
            ? new Dictionary<string, StoreServiceRow>(StringComparer.Ordinal)
            : publishedServices
                .Where(s => serviceIdSet.Contains(s.Id))
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);

        return (publishedProductCountByStore, publishedServiceCountByStore, productsById, servicesById);
    }

    private static JsonObject BuildStoreBadgeJson(StoreRow row)
    {
        var node = new JsonObject
        {
            ["id"] = row.Id,
            ["name"] = row.Name,
            ["verified"] = row.Verified,
            ["transportIncluded"] = row.TransportIncluded,
            ["trustScore"] = row.TrustScore,
            ["ownerUserId"] = row.OwnerUserId,
        };
        if (!string.IsNullOrEmpty(row.AvatarUrl))
            node["avatarUrl"] = row.AvatarUrl;

        try { node["categories"] = JsonNode.Parse(row.CategoriesJson) ?? new JsonArray(); }
        catch { node["categories"] = new JsonArray(); }

        if (row.LocationLatitude is { } la && row.LocationLongitude is { } lo)
            node["location"] = new JsonObject { ["lat"] = la, ["lng"] = lo };

        return node;
    }

    private static JsonObject BuildProductOfferJson(StoreProductRow p)
    {
        var o = new JsonObject
        {
            ["id"] = p.Id,
            ["kind"] = "product",
            ["name"] = p.Name,
            ["category"] = p.Category,
            ["price"] = p.Price,
            ["currency"] = p.MonedaPrecio,
        };
        try { o["acceptedCurrencies"] = JsonNode.Parse(p.MonedasJson) ?? new JsonArray(); }
        catch { o["acceptedCurrencies"] = new JsonArray(); }
        try { o["photoUrls"] = JsonNode.Parse(p.PhotoUrlsJson) ?? new JsonArray(); }
        catch { o["photoUrls"] = new JsonArray(); }
        if (!string.IsNullOrEmpty(p.ShortDescription))
            o["shortDescription"] = p.ShortDescription;
        return o;
    }

    private static JsonObject BuildServiceOfferJson(StoreServiceRow s)
    {
        var o = new JsonObject
        {
            ["id"] = s.Id,
            ["kind"] = "service",
            ["category"] = s.Category,
            ["tipoServicio"] = s.TipoServicio,
        };
        try { o["acceptedCurrencies"] = JsonNode.Parse(s.MonedasJson) ?? new JsonArray(); }
        catch { o["acceptedCurrencies"] = new JsonArray(); }
        try { o["photoUrls"] = JsonNode.Parse(s.PhotoUrlsJson) ?? new JsonArray(); }
        catch { o["photoUrls"] = new JsonArray(); }
        if (!string.IsNullOrEmpty(s.Descripcion))
            o["descripcion"] = s.Descripcion;
        return o;
    }
}
