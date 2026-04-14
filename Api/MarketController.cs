using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Search;

namespace VibeTrade.Backend.Api;

/// <summary>Mercado: workspace (GET), tiendas/catálogo/consultas (PUT por recurso), búsqueda y detalle.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(
    IMarketWorkspaceService marketWorkspace,
    IMarketCatalogSyncService catalog,
    IAuthService auth,
    IRecommendationService recommendations,
    IElasticsearchStoreSearchQuery storeSearchQuery,
    AppDbContext db) : ControllerBase
{
    public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

    public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

    public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

    public sealed record PostInquiryAskedBy(string Id, string Name, int TrustScore);

    public sealed record PostInquiryBody(string OfferId, string Question, PostInquiryAskedBy AskedBy, long? CreatedAt);

    public sealed record CatalogSearchItem(
        string Kind,
        JsonObject Store,
        JsonObject? Offer,
        long? PublishedProducts,
        long? PublishedServices,
        double? DistanceKm);

    public sealed record StoreSearchResponse(
        IReadOnlyList<CatalogSearchItem> Items,
        bool hasMore,
        int Offset,
        int Limit);

    public sealed record StoreAutocompleteResponse(IReadOnlyList<string> Suggestions);

    /// <summary>Categorías permitidas para productos, servicios y sugerencias en acuerdos (misma lista).</summary>
    [HttpGet("catalog-categories")]
    [ProducesResponseType(typeof(CatalogCategoriesResponse), StatusCodes.Status200OK)]
    public ActionResult<CatalogCategoriesResponse> GetCatalogCategories()
    {
        return Ok(new CatalogCategoriesResponse(CatalogCategories.ProductAndService));
    }

    /// <summary>Monedas permitidas en fichas de catálogo (misma lista para productos y servicios).</summary>
    [HttpGet("currencies")]
    [ProducesResponseType(typeof(CurrenciesResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrenciesResponse> GetCurrencies()
    {
        return Ok(new CurrenciesResponse(CatalogCurrencies.All));
    }

    /// <summary>
    /// Busca en Elasticsearch (índice de catálogo): tiendas, productos y servicios (nombre, categoría, distancia km).
    /// Sin Elasticsearch o si la búsqueda falla: respuesta vacía. Tolerancia a typos: fuzzy de Lucene en la query (<c>ElasticsearchStoreSearchQuery</c>).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("stores/search")]
    [ProducesResponseType(typeof(StoreSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreSearchResponse>> SearchStores(
        [FromQuery] string? name,
        [FromQuery] string? category,
        [FromQuery] string? kinds,
        [FromQuery] int? trustMin,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? km,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var ctx = ParseStoreSearchContext(name, category, kinds, trustMin, lat, lng, km, limit, offset);

        var esResponse = await TrySearchStoresViaElasticsearchAsync(ctx, cancellationToken);
        if (esResponse is not null)
            return Ok(esResponse);

        return Ok(new StoreSearchResponse([], false, ctx.Skip, ctx.Take));
    }

    /// <summary>
    /// Autocomplete público para el input "Buscar" del catálogo (tiendas + ofertas publicadas).
    /// Devuelve sugerencias de texto para <c>name</c> (no ejecuta búsqueda completa).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("stores/autocomplete")]
    [ProducesResponseType(typeof(StoreAutocompleteResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreAutocompleteResponse>> AutocompleteStores(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? kinds,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var query = (q ?? "").Trim();
        if (query.Length < 2)
            return Ok(new StoreAutocompleteResponse(Array.Empty<string>()));
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

        static IReadOnlyList<string> BuildIlikePatterns(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length == 0) return Array.Empty<string>();
            if (q.Length > 80) q = q[..80];

            var patterns = new List<string>(12) { $"%{q}%" };

            // Fallback leve para 1 typo: reemplazar 1 char por '_' (comodín 1-char).
            // Esto permite hav -> ha_ (match hab...), havana -> ha_ana (match habana), etc.
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

        var patterns = BuildIlikePatterns(query);
        var like = patterns[0];

        // Candidatos (SQL): usamos LIKE amplio para no traer toda la tabla.
        // Luego rankeamos en memoria con tolerancia a typos (edit distance + prefix/contains).
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

            // Filtro de categorías para tiendas (in-memory) para evitar expresiones no traducibles (Any + Like).
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

        static string Fold(string s) => (s ?? "").Trim().ToLowerInvariant();

        static int EditDistanceLevenshtein(string a, string b, int maxDistance)
        {
            // Bounded Levenshtein to keep it cheap.
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            if (Math.Abs(a.Length - b.Length) > maxDistance) return maxDistance + 1;

            // Ensure a is shorter.
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

        static int ScoreCandidate(string qFold, string cand)
        {
            var c = (cand ?? "").Trim();
            if (c.Length == 0) return int.MinValue;
            var cf = Fold(c);
            if (cf == qFold) return 10_000;
            if (cf.StartsWith(qFold, StringComparison.Ordinal)) return 9_000 - (cf.Length - qFold.Length);
            if (cf.Contains(qFold, StringComparison.Ordinal)) return 7_500 - Math.Max(0, cf.Length - qFold.Length);

            // typo tolerance (bounded).
            var maxD = qFold.Length <= 4 ? 1 : qFold.Length <= 8 ? 2 : 3;
            var d = EditDistanceLevenshtein(qFold, cf, maxD);
            if (d <= maxD) return 6_000 - d * 500 - Math.Abs(cf.Length - qFold.Length) * 10;
            return int.MinValue;
        }

        var qFold = Fold(query);

        static IEnumerable<string> Clean(IEnumerable<string> xs) =>
            xs.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0);

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

        return Ok(new StoreAutocompleteResponse(merged));
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

    private async Task<StoreSearchResponse?> TrySearchStoresViaElasticsearchAsync(
        StoreSearchContext ctx,
        CancellationToken cancellationToken)
    {
        if (!storeSearchQuery.IsConfigured)
            return null;

        return await SearchStoresViaElasticsearchFillPageAsync(ctx, cancellationToken);
    }

    private async Task<StoreSearchResponse?> SearchStoresViaElasticsearchFillPageAsync(
        StoreSearchContext ctx,
        CancellationToken cancellationToken)
    {
        // Estrategia: pedir ES en páginas de `take` y materializar;
        // si por cualquier motivo se descartan hits (desync, borrados, etc.),
        // seguir pidiendo páginas siguientes hasta llenar `take` o agotar hits.
        var want = ctx.Take;
        var skipCursor = ctx.Skip;
        var chunk = ctx.Take;

        ElasticsearchStoreSearchResult? first = null;
        var items = new List<CatalogSearchItem>(want);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

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

        while (items.Count < want && es.Hits.Count > 0)
        {
            es = await storeSearchQuery.SearchAsync(
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
        bool hasMore = items.Count == want;
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

            if( ctx?.TrustMin is not null && (row.TrustScore < ctx.TrustMin!.Value))
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

    /// <summary>Obtiene el snapshot actual del mercado; si la base está vacía, aplica seed embebido.</summary>
    [HttpGet("workspace")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkspace(CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var json = doc.RootElement.GetRawText();
        return Content(json, "application/json");
    }

    /// <summary>Lista de tiendas desde tablas relacionales (<c>stores</c>).</summary>
    [HttpGet("workspace/stores")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceStores(CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetStoresSnapshotAsync(cancellationToken);
        return Content(doc.RootElement.GetRawText(), "application/json");
    }

    /// <summary>
    /// Metadatos de tienda (perfil). Cuerpo: ficha plana con <c>id</c> (nombre, categorías, etc.) o legado
    /// <c>{"stores":{"&lt;id&gt;":{...}}}</c>. Se fusiona con el workspace en servidor.
    /// </summary>
    [HttpPut("workspace/stores")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutWorkspaceStores([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        JsonDocument toSave;
        try
        {
            toSave = NormalizeWorkspaceStoresPutBody(body);
        }
        catch (ArgumentException)
        {
            body.Dispose();
            return BadRequest(new { error = "invalid_stores_body", message = "Indicá la tienda con un campo \"id\" o usá la forma \"stores\".{...}." });
        }

        try
        {
            await marketWorkspace.SaveStoreProfilesAsync(toSave, cancellationToken);
        }
        catch (DuplicateStoreNameException)
        {
            return Conflict(new { error = "duplicate_store_name", message = "Ya existe una tienda con ese nombre en la plataforma." });
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>
    /// Catálogo (productos/servicios y pitch). Cuerpo parcial:
    /// <c>{"stores":{...},"storeCatalogs":{"&lt;id&gt;":{...}}}</c>. Se fusiona con el workspace en servidor.
    /// </summary>
    [HttpPut("workspace/catalogs")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PutWorkspaceCatalogs([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveStoreCatalogsAsync(body, cancellationToken);
        }
        catch (DuplicateStoreNameException)
        {
            return Conflict(new { error = "duplicate_store_name", message = "Ya existe una tienda con ese nombre en la plataforma." });
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>Persiste una sola ficha de producto (cuerpo = JSON del producto, sin envolver en catálogo).</summary>
    [HttpPut("stores/{storeId}/products/{productId}")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutStoreProduct(
        string storeId,
        string productId,
        [FromBody] JsonDocument body,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreProductAsync(storeId, productId, userId, body.RootElement, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    [HttpDelete("stores/{storeId}/products/{productId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStoreProduct(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreProductAsync(storeId, productId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    /// <summary>Persiste una sola ficha de servicio (cuerpo = JSON del servicio).</summary>
    [HttpPut("stores/{storeId}/services/{serviceId}")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutStoreService(
        string storeId,
        string serviceId,
        [FromBody] JsonDocument body,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        try
        {
            var r = await catalog.UpsertStoreServiceAsync(storeId, serviceId, userId, body.RootElement, cancellationToken);
            return MapCatalogUpsert(r);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }
    }

    [HttpDelete("stores/{storeId}/services/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStoreService(
        string storeId,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized(new { error = "unauthorized", message = "Sesión requerida." });
        var r = await catalog.DeleteStoreServiceAsync(storeId, serviceId, userId, cancellationToken);
        return MapCatalogUpsert(r);
    }

    /// <summary>Añade una pregunta pública a la oferta (producto/servicio) identificada por <paramref name="body"/>.OfferId.</summary>
    [HttpPost("inquiries")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostInquiry([FromBody] PostInquiryBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.OfferId) || string.IsNullOrWhiteSpace(body.Question))
            return BadRequest(new { error = "invalid_inquiry", message = "Indicá la oferta y el texto de la pregunta." });
        var askedById = (body.AskedBy?.Id ?? "").Trim();
        var askedByName = (body.AskedBy?.Name ?? "").Trim();
        var askedByTrust = body.AskedBy?.TrustScore ?? 0;
        var isAnonymous = askedById.Length == 0 || string.Equals(askedById, "guest", StringComparison.OrdinalIgnoreCase);
        if (isAnonymous)
        {
            askedById = "guest";
            if (askedByName.Length == 0) askedByName = "Anónimo";
            askedByTrust = 0;
        }

        var q = body.Question.Trim();
        if (q.Length > 12_000)
            return BadRequest(new { error = "invalid_inquiry", message = "La pregunta es demasiado larga." });

        try
        {
            var item = await catalog.AppendOfferInquiryAsync(
                body.OfferId.Trim(),
                q,
                askedById,
                askedByName,
                askedByTrust,
                body.CreatedAt,
                cancellationToken);
            if (item is null)
                return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });

            if (!isAnonymous)
            {
                await recommendations.RecordInteractionAsync(
                    askedById,
                    body.OfferId.Trim(),
                    RecommendationInteractionType.Inquiry,
                    cancellationToken);
            }

            return Content(item.ToJsonString(), "application/json");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_inquiry", message = ex.Message });
        }
    }

    /// <summary>Sincronización masiva de <c>offers[*].qa</c> (legado / herramientas).</summary>
    [HttpPut("inquiries")]
    [HttpPut("workspace/inquiries")]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PutWorkspaceInquiries([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveOfferInquiriesAsync(body, cancellationToken);
        }
        catch (CatalogCurrencyValidationException ex)
        {
            return BadRequest(new { error = "catalog_currency_invalid", message = ex.Message });
        }

        return Ok();
    }

    /// <summary>
    /// Detalle de tienda + catálogo (carga bajo demanda). El cuerpo identifica al visitante para futura personalización.
    /// </summary>
    [HttpPost("stores/{storeId}/detail")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostStoreDetail(
        string storeId,
        [FromBody] StoreDetailBody? body,
        CancellationToken cancellationToken)
    {
        using var doc = await marketWorkspace.GetStoreDetailAsync(storeId, cancellationToken);
        if (doc is null)
            return NotFound();
        var root = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        if (body is not null && (body.ViewerUserId is not null || body.ViewerRole is not null))
        {
            root["viewer"] = new JsonObject
            {
                ["userId"] = body.ViewerUserId is null ? null : JsonValue.Create(body.ViewerUserId),
                ["role"] = body.ViewerRole is null ? null : JsonValue.Create(body.ViewerRole),
            };
        }

        return Content(root.ToJsonString(), "application/json");
    }

    /// <summary>
    /// Acepta <c>{"id":"...","name":...}</c> y lo convierte al patch interno <c>stores[id]</c>.
    /// Si ya viene <c>stores</c>, se devuelve el mismo documento.
    /// </summary>
    private static JsonDocument NormalizeWorkspaceStoresPutBody(JsonDocument body)
    {
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Root must be an object.", nameof(body));

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var storeId = idEl.GetString();
            if (string.IsNullOrWhiteSpace(storeId))
                throw new ArgumentException("Store id is empty.", nameof(body));

            var wrapped = new JsonObject
            {
                ["stores"] = new JsonObject { [storeId] = JsonNode.Parse(root.GetRawText())! },
            };
            var doc = JsonDocument.Parse(wrapped.ToJsonString());
            body.Dispose();
            return doc;
        }

        throw new ArgumentException("Missing stores object or store id.", nameof(body));
    }

    private string? GetBearerUserId()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHdr))
            return null;
        if (!auth.TryGetUserByToken(authHdr, out var user))
            return null;
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return null;
        var id = idEl.GetString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private IActionResult MapCatalogUpsert(StoreCatalogUpsertResult r) =>
        r switch
        {
            StoreCatalogUpsertResult.Ok => Ok(),
            StoreCatalogUpsertResult.Unauthorized => Unauthorized(new { error = "unauthorized", message = "Sesión requerida." }),
            StoreCatalogUpsertResult.StoreNotFound => NotFound(),
            StoreCatalogUpsertResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            StoreCatalogUpsertResult.IdMismatch => BadRequest(new { error = "id_mismatch", message = "El id del cuerpo no coincide con la ruta." }),
            StoreCatalogUpsertResult.EntityNotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
}
