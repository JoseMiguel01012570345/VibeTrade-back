using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Search.Elasticsearch;
using VibeTrade.Backend.Features.Search.Interfaces;

namespace VibeTrade.Backend.Features.Search;

public sealed class MarketCatalogStoreSearchService(
    IElasticsearchStoreSearchQuery storeSearchQuery,
    AppDbContext db,
    ILogger<MarketCatalogStoreSearchService> logger,
    IOfferService offerService) : IMarketCatalogStoreSearchService
{
    private static string OfferIdForDedupeKey(CatalogSearchItemOffer? offer)
    {
        if (offer is null) return "";
        if (offer.Product is { } p) return p.Id;
        if (offer.Service is { } s) return s.Id;
        if (offer.Emergent is { } e) return e.Id;
        return "";
    }

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
        // Preprocess and validate input
        var (query, take, kindList, catParts, catLowerParts) = PreprocessAutocompleteInputs(q, category, kinds, limit);

        if (query.Length < 2)
            return new StoreAutocompleteResponse(Array.Empty<string>());

        const int MaxCandidatesPerKind = 120;

        // Fetch candidates
        var storeCandidates = kindList.Contains(CatalogSearchKinds.Store, StringComparer.Ordinal)
            ? await GetStoreCandidatesAsync(query, catLowerParts, MaxCandidatesPerKind, cancellationToken)
            : new List<string>();

        var productCandidates = kindList.Contains(CatalogSearchKinds.Product, StringComparer.Ordinal)
            ? await GetProductCandidatesAsync(query, catParts, catLowerParts, MaxCandidatesPerKind, cancellationToken)
            : new List<string>();

        var serviceCandidates = kindList.Contains(CatalogSearchKinds.Service, StringComparer.Ordinal)
            ? await GetServiceCandidatesAsync(query, catParts, catLowerParts, MaxCandidatesPerKind, cancellationToken)
            : new List<string>();

        // LogInformation: LogDebug no aparece con "Default": "Information" en appsettings.
        logger.LogInformation(
            "Autocomplete query=\"{Query}\" counts stores={StoreCount} products={ProductCount} services={ServiceCount}",
            query.Length > 40 ? query[..40] + "…" : query,
            storeCandidates.Count,
            productCandidates.Count,
            serviceCandidates.Count);

        var merged = Clean(storeCandidates)
            .Concat(Clean(productCandidates))
            .Concat(Clean(serviceCandidates))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        return new StoreAutocompleteResponse(merged);
    }

    private (string query, int take, IReadOnlyList<string> kindList, string[] catParts, string[] catLowerParts)
        PreprocessAutocompleteInputs(string? q, string? category, string? kinds, int? limit)
    {
        var query = (q ?? "").Trim();
        if (query.Length > 80)
            query = query[..80];
        var take = Math.Clamp(limit ?? 10, 1, 25);

        var kindList = ParseKindsParam(kinds);

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

        return (query, take, kindList, catParts, catLowerParts);
    }

    /// <summary>Patrón ILIKE prefijo seguro (sin metacaracteres %/_ del usuario).</summary>
    private static string AutocompleteLikePrefix(string trimmedQuery)
    {
        var sb = new StringBuilder(trimmedQuery.Length + 1);
        foreach (var c in trimmedQuery)
        {
            if (c is '%' or '_') continue;
            sb.Append(c);
        }
        var core = sb.ToString();
        if (core.Length == 0) return "%";
        return $"{core}%";
    }

    /// <summary>
    /// Patrones <c>ILIKE</c> con un <c>_</c> por posición (p. ej. «hav» → «%ha_%») más el substring «%{q}%».
    /// Más fiable que solo <c>similarity()</c> para texto corto vs nombres largos.
    /// </summary>
    private static IReadOnlyList<string> BuildIlikePatterns(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length == 0) return Array.Empty<string>();
        if (q.Length > 80) q = q[..80];

        var patterns = new List<string>(12);

        if (q.Length is >= 3 and <= 10)
        {
            for (var i = 0; i < q.Length && patterns.Count < 9; i++)
            {
                var one = q.ToCharArray();
                one[i] = '_';
                patterns.Add($"%{new string(one)}%");
            }
        }

        patterns.Add($"%{q}%");
        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int AutocompletePerPatternTake(int maxTotal, int patternCount) =>
        patternCount <= 0
            ? maxTotal
            : Math.Clamp(maxTotal / patternCount, 40, maxTotal);

    private async Task<List<string>> GetStoreCandidatesAsync(
        string query,
        string[] catLowerParts,
        int maxCandidatesPerKind,
        CancellationToken cancellationToken)
    {
        var likePrefix = AutocompleteLikePrefix(query);
        var patterns = BuildIlikePatterns(query);
        var perPattern = AutocompletePerPatternTake(maxCandidatesPerKind, patterns.Count);
        var storeRows = new List<(string? Name, IReadOnlyList<string> Categories)>(maxCandidatesPerKind);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pat in patterns)
        {
            if (seenNames.Count >= maxCandidatesPerKind) break;
            var remaining = maxCandidatesPerKind - seenNames.Count;
            var takeThis = Math.Min(perPattern, remaining);
            var qStores = db.Stores.AsNoTracking()
                .Where(s =>
                    (s.Name != null && (
                        EF.Functions.ILike(s.Name, likePrefix) ||
                        EF.Functions.ILike(s.Name, pat))) ||
                    (s.NormalizedName != null && (
                        EF.Functions.ILike(s.NormalizedName, likePrefix) ||
                        EF.Functions.ILike(s.NormalizedName, pat))));

            var slice = await qStores
                .OrderByDescending(s => s.TrustScore)
                .Select(s => new { s.Name, s.Categories })
                .Take(takeThis)
                .ToListAsync(cancellationToken);

            foreach (var r in slice)
            {
                var n = r.Name;
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (!seenNames.Add(n)) continue;
                storeRows.Add((r.Name, r.Categories));
            }
        }

        if (catLowerParts.Length == 0)
            return storeRows.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        else
            return storeRows
                .Where(x =>
                {
                    var cj = string.Join(" ", x.Categories ?? Array.Empty<string>()).ToLowerInvariant();
                    return cj.Length > 0 && catLowerParts.Any(c => cj.Contains(c, StringComparison.Ordinal));
                })
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
    }

    private async Task<List<string>> GetProductCandidatesAsync(
        string query,
        string[] catParts,
        string[] catLowerParts,
        int maxCandidatesPerKind,
        CancellationToken cancellationToken)
    {
        var likePrefix = AutocompleteLikePrefix(query);
        var patterns = BuildIlikePatterns(query);
        var perPattern = AutocompletePerPatternTake(maxCandidatesPerKind, patterns.Count);
        var outNames = new List<string>(maxCandidatesPerKind);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pat in patterns)
        {
            if (seen.Count >= maxCandidatesPerKind) break;
            var remaining = maxCandidatesPerKind - seen.Count;
            var takeThis = Math.Min(perPattern, remaining);
            var qProducts = db.StoreProducts.AsNoTracking()
                .Where(p => p.Published &&
                            (
                                EF.Functions.ILike(p.Name, likePrefix) ||
                                EF.Functions.ILike(p.Name, pat) ||
                                (p.Model != null && EF.Functions.ILike(p.Model, likePrefix)) ||
                                (p.Model != null && EF.Functions.ILike(p.Model, pat))));
            if (catParts.Length > 0)
                qProducts = qProducts.Where(p => catLowerParts.Contains((p.Category ?? "").ToLower()));

            var slice = await qProducts
                .OrderBy(p => p.Name)
                .Select(p => p.Name)
                .Take(takeThis)
                .ToListAsync(cancellationToken);

            foreach (var name in slice)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (seen.Add(name))
                    outNames.Add(name);
            }
        }

        return outNames;
    }

    private async Task<List<string>> GetServiceCandidatesAsync(
        string query,
        string[] catParts,
        string[] catLowerParts,
        int maxCandidatesPerKind,
        CancellationToken cancellationToken)
    {
        var likePrefix = AutocompleteLikePrefix(query);
        var patterns = BuildIlikePatterns(query);
        var perPattern = AutocompletePerPatternTake(maxCandidatesPerKind, patterns.Count);
        var outNames = new List<string>(maxCandidatesPerKind);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pat in patterns)
        {
            if (seen.Count >= maxCandidatesPerKind) break;
            var remaining = maxCandidatesPerKind - seen.Count;
            var takeThis = Math.Min(perPattern, remaining);
            var qServices = db.StoreServices.AsNoTracking()
                .Where(s => (s.Published == null || s.Published == true) &&
                            (
                                EF.Functions.ILike(s.TipoServicio, likePrefix) ||
                                EF.Functions.ILike(s.TipoServicio, pat) ||
                                EF.Functions.ILike(s.Category, likePrefix) ||
                                EF.Functions.ILike(s.Category, pat)));
            if (catParts.Length > 0)
                qServices = qServices.Where(s => catLowerParts.Contains((s.Category ?? "").ToLower()));

            var slice = await qServices
                .OrderBy(s => s.TipoServicio)
                .Select(s => s.TipoServicio)
                .Take(takeThis)
                .ToListAsync(cancellationToken);

            foreach (var name in slice)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (seen.Add(name))
                    outNames.Add(name);
            }
        }

        return outNames;
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
            return
            [
                CatalogSearchKinds.Store,
                CatalogSearchKinds.Product,
                CatalogSearchKinds.Service,
                CatalogSearchKinds.Emergent,
            ];

        var parts = kinds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var ok = new List<string>(3);
        foreach (var p in parts)
        {
            if (p is CatalogSearchKinds.Store
                or CatalogSearchKinds.Product
                or CatalogSearchKinds.Service
                or CatalogSearchKinds.Emergent)
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
                var offerId = OfferIdForDedupeKey(it.Offer);
                var key = it.Kind == CatalogSearchKinds.Store
                    ? $"store:{it.Store.Id}"
                    : string.IsNullOrEmpty(offerId)
                        ? $"x:{it.Store.Id}"
                        : $"{it.Kind}:{offerId}";

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

        var emergentPublicationIds = hits
            .Where(h => h.Kind == CatalogSearchKinds.Emergent && !string.IsNullOrEmpty(h.OfferId))
            .Select(h => h.OfferId!)
            .Distinct()
            .ToList();
        var emergentsById = emergentPublicationIds.Count == 0
            ? new Dictionary<string, EmergentOfferRow>(StringComparer.Ordinal)
            : await db.EmergentOffers.AsNoTracking()
                .Where(e => emergentPublicationIds.Contains(e.Id)
                    && e.RetractedAtUtc == null
                    && db.ChatRouteSheets.Any(r =>
                        r.ThreadId == e.ThreadId
                        && r.RouteSheetId == e.RouteSheetId
                        && r.DeletedAtUtc == null
                        && r.PublishedToPlatform))
                .ToDictionaryAsync(e => e.Id, cancellationToken);

        var emergentLiveSheetsByKey = emergentPublicationIds.Count == 0
            ? new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal)
            : await RecommendationBatchOfferLoader.LoadLiveRouteSheetsForEmergentsAsync(
                db,
                emergentsById.Values.ToList(),
                cancellationToken);

        foreach (var em in emergentsById.Values)
        {
            var oid = (em.OfferId ?? "").Trim();
            if (oid.Length == 0)
                continue;
            if (!productIds.Contains(oid, StringComparer.Ordinal))
                productIds.Add(oid);
            if (!serviceIds.Contains(oid, StringComparer.Ordinal))
                serviceIds.Add(oid);
        }

        var (publishedProductCountByStore, publishedServiceCountByStore, productsById, servicesById) =
            await LoadPublishedCountsForStoreIdsAsync(storeIds, productIds, serviceIds, cancellationToken);

        var items = new List<CatalogSearchItem>(hits.Count);
        foreach (var hit in hits)
        {
            if (!rows.TryGetValue(hit.StoreId, out var row))
                continue;
            publishedProductCountByStore.TryGetValue(row.Id, out var pp);
            publishedServiceCountByStore.TryGetValue(row.Id, out var ps);
            var storeBadge = BuildStoreBadge(row);

            if (ctx.TrustMin is not null && row.TrustScore < ctx.TrustMin.Value)
                continue;

            if (hit.Kind == CatalogSearchKinds.Store)
            {
                items.Add(new CatalogSearchItem(
                    CatalogSearchKinds.Store,
                    storeBadge,
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
                    storeBadge,
                    new CatalogSearchItemOffer { Product = BuildSlimProductOffer(pr) },
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
                    storeBadge,
                    new CatalogSearchItemOffer { Service = BuildSlimServiceOffer(sv) },
                    pp,
                    ps,
                    hit.DistanceKm));
                continue;
            }

            if (hit.Kind == CatalogSearchKinds.Emergent
                && hit.OfferId is { } emoId
                && emergentsById.TryGetValue(emoId, out var emRow))
            {
                productsById.TryGetValue(emRow.OfferId, out var emPr);
                servicesById.TryGetValue(emRow.OfferId, out var emSv);
                if (emPr is not null && !string.Equals(emPr.StoreId, row.Id, StringComparison.Ordinal))
                    continue;
                if (emPr is null && emSv is not null
                                 && !string.Equals(emSv.StoreId, row.Id, StringComparison.Ordinal))
                    continue;

                var orphanFallback = emPr is null && emSv is null ? row.Id : null;
                emergentLiveSheetsByKey.TryGetValue(
                    OfferUtils.EmergentOfferRouteSheetKey(emRow.ThreadId, emRow.RouteSheetId),
                    out var liveRoutePayload);
                var emergent = offerService.CreateEmergentRoutePublication(
                    emRow,
                    emPr,
                    emSv,
                    orphanFallback,
                    liveRoutePayload);
                items.Add(new CatalogSearchItem(
                    CatalogSearchKinds.Emergent,
                    storeBadge,
                    new CatalogSearchItemOffer { Emergent = emergent },
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

    private static CatalogSearchStoreBadge BuildStoreBadge(StoreRow row)
    {
        CatalogSearchStoreLocation? loc = null;
        if (row.LocationLatitude is { } la && row.LocationLongitude is { } lo)
            loc = new CatalogSearchStoreLocation(la, lo);
        return new CatalogSearchStoreBadge(
            row.Id,
            row.Name,
            row.Verified,
            row.TransportIncluded,
            row.TrustScore,
            row.OwnerUserId,
            string.IsNullOrEmpty(row.AvatarUrl) ? null : row.AvatarUrl,
            ParseStoreCategories(row.Categories),
            loc,
            string.IsNullOrWhiteSpace(row.Pitch) ? null : row.Pitch.Trim(),
            string.IsNullOrWhiteSpace(row.WebsiteUrl) ? null : row.WebsiteUrl.Trim());
    }

    private static IReadOnlyList<string> ParseStoreCategories(IReadOnlyList<string>? categories) =>
        CatalogJsonColumnParsing.StringListOrEmpty(categories);

    private static CatalogSearchSlimProductOffer BuildSlimProductOffer(StoreProductRow p) =>
        new()
        {
            Id = p.Id,
            Kind = "product",
            Name = p.Name,
            Category = p.Category,
            Price = p.Price,
            Currency = p.MonedaPrecio,
            AcceptedCurrencies = CatalogJsonColumnParsing.StringListOrEmpty(p.Monedas),
            PhotoUrls = CatalogJsonColumnParsing.StringListOrEmpty(p.PhotoUrls),
            ShortDescription = string.IsNullOrEmpty(p.ShortDescription) ? null : p.ShortDescription,
        };

    private static CatalogSearchSlimServiceOffer BuildSlimServiceOffer(StoreServiceRow s) =>
        new()
        {
            Id = s.Id,
            Kind = "service",
            Category = s.Category,
            TipoServicio = s.TipoServicio,
            AcceptedCurrencies = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas),
            PhotoUrls = CatalogJsonColumnParsing.StringListOrEmpty(s.PhotoUrls),
            Descripcion = string.IsNullOrEmpty(s.Descripcion) ? null : s.Descripcion,
        };
}
