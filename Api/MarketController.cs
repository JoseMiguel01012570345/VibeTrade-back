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
        int TotalCount,
        int Offset,
        int Limit);

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
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? km,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var ctx = ParseStoreSearchContext(name, category, kinds, lat, lng, km, limit, offset);

        var esResponse = await TrySearchStoresViaElasticsearchAsync(ctx, cancellationToken);
        if (esResponse is not null)
            return Ok(esResponse);

        return Ok(new StoreSearchResponse([], 0, ctx.Skip, ctx.Take));
    }

    private sealed record StoreSearchContext(
        int Take,
        int Skip,
        string? NameParam,
        string? CategoryParam,
        IReadOnlyList<string> Kinds,
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

        var hasDistanceFilter = lat.HasValue && lng.HasValue && km.HasValue && km.Value > 0;
        if (hasDistanceFilter)
        {
            if (!double.IsFinite(lat!.Value) || lat.Value is < -90 or > 90) hasDistanceFilter = false;
            if (!double.IsFinite(lng!.Value) || lng.Value is < -180 or > 180) hasDistanceFilter = false;
            if (!double.IsFinite(km!.Value) || km.Value is <= 0 or > 25_000) hasDistanceFilter = false;
        }

        return new StoreSearchContext(take, skip, name, category, kindList, nameQ, catQ, hasDistanceFilter, lat, lng, km);
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

        var es = await storeSearchQuery.SearchAsync(
            ctx.NameQuery,
            ctx.CategoryQuery,
            ctx.Kinds,
            ctx.HasDistanceFilter,
            ctx.Lat ?? 0,
            ctx.Lng ?? 0,
            ctx.Km ?? 0,
            ctx.Skip,
            ctx.Take,
            cancellationToken);
        if (es is null)
            return null;

        return await BuildCatalogSearchResponseFromElasticsearchAsync(es, ctx, cancellationToken);
    }

    private async Task<StoreSearchResponse> BuildCatalogSearchResponseFromElasticsearchAsync(
        ElasticsearchStoreSearchResult es,
        StoreSearchContext ctx,
        CancellationToken cancellationToken)
    {
        var total = (int)Math.Min(es.TotalCount, int.MaxValue);
        if (es.Hits.Count == 0)
            return new StoreSearchResponse([], total, ctx.Skip, ctx.Take);

        var storeIds = es.Hits.Select(h => h.StoreId).Distinct().ToList();
        var rows = await db.Stores.AsNoTracking()
            .Where(s => storeIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var (pubProducts, pubServices) = await LoadPublishedCountsForStoreIdsAsync(storeIds, cancellationToken);

        var productIds = es.Hits
            .Where(h => h.Kind == CatalogSearchKinds.Product && !string.IsNullOrEmpty(h.OfferId))
            .Select(h => h.OfferId!)
            .Distinct()
            .ToList();
        var serviceIds = es.Hits
            .Where(h => h.Kind == CatalogSearchKinds.Service && !string.IsNullOrEmpty(h.OfferId))
            .Select(h => h.OfferId!)
            .Distinct()
            .ToList();

        var products = productIds.Count == 0
            ? new Dictionary<string, StoreProductRow>()
            : await db.StoreProducts.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        var services = serviceIds.Count == 0
            ? new Dictionary<string, StoreServiceRow>()
            : await db.StoreServices.AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken);

        var items = new List<CatalogSearchItem>(es.Hits.Count);
        foreach (var hit in es.Hits)
        {
            if (!rows.TryGetValue(hit.StoreId, out var row))
                continue;
            pubProducts.TryGetValue(row.Id, out var pp);
            pubServices.TryGetValue(row.Id, out var ps);
            var storeJson = BuildStoreBadgeJson(row);

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
                                                     && products.TryGetValue(pid, out var pr))
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
                                                      && services.TryGetValue(sid, out var sv))
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

        return new StoreSearchResponse(items, total, ctx.Skip, ctx.Take);
    }

    private async Task<(Dictionary<string, int> Products, Dictionary<string, int> Services)> LoadPublishedCountsForStoreIdsAsync(
        IReadOnlyCollection<string> storeIds,
        CancellationToken cancellationToken)
    {
        var pubProducts = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published && storeIds.Contains(p.StoreId))
            .GroupBy(p => p.StoreId)
            .Select(g => new { StoreId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Cnt, cancellationToken);

        var pubServices = await db.StoreServices.AsNoTracking()
            .Where(s => (s.Published == null || s.Published == true) && storeIds.Contains(s.StoreId))
            .GroupBy(s => s.StoreId)
            .Select(g => new { StoreId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Cnt, cancellationToken);

        return (pubProducts, pubServices);
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
        if (body.AskedBy is null || string.IsNullOrWhiteSpace(body.AskedBy.Id))
            return BadRequest(new { error = "invalid_inquiry", message = "Faltan datos de quien pregunta." });

        var q = body.Question.Trim();
        if (q.Length > 12_000)
            return BadRequest(new { error = "invalid_inquiry", message = "La pregunta es demasiado larga." });

        try
        {
            var item = await catalog.AppendOfferInquiryAsync(
                body.OfferId.Trim(),
                q,
                body.AskedBy.Id.Trim(),
                (body.AskedBy.Name ?? "").Trim(),
                body.AskedBy.TrustScore,
                body.CreatedAt,
                cancellationToken);
            if (item is null)
                return NotFound(new { error = "offer_not_found", message = "No existe una oferta con ese identificador." });

            await recommendations.RecordInteractionAsync(
                body.AskedBy.Id.Trim(),
                body.OfferId.Trim(),
                RecommendationInteractionType.Inquiry,
                cancellationToken);

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
