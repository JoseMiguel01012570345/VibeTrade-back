using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Api;

/// <summary>Mercado: workspace (GET), tiendas/catálogo/consultas (PUT por recurso), búsqueda y detalle.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(
    IMarketWorkspaceService marketWorkspace,
    IMarketCatalogSyncService catalog,
    IAuthService auth,
    AppDbContext db) : ControllerBase
{
    public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

    public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

    public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

    public sealed record PostInquiryAskedBy(string Id, string Name, int TrustScore);

    public sealed record PostInquiryBody(string OfferId, string Question, PostInquiryAskedBy AskedBy, long? CreatedAt);

    public sealed record StoreSearchItem(JsonObject Store, int PublishedProducts, int PublishedServices, double? DistanceKm);

    public sealed record StoreSearchResponse(IReadOnlyList<StoreSearchItem> Items);

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
    /// Busca tiendas en toda la base de datos por nombre, categoría, vitrina (productos/servicios) y distancia (km).
    /// Requiere que la tienda tenga ubicación para aplicar distancia.
    /// </summary>
    [HttpGet("stores/search")]
    [ProducesResponseType(typeof(StoreSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreSearchResponse>> SearchStores(
        [FromQuery] string? name,
        [FromQuery] string? category,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? km,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 40, 1, 200);

        var nameQ = (name ?? "").Trim();
        var catQ = (category ?? "").Trim();

        var hasDistanceFilter = lat.HasValue && lng.HasValue && km.HasValue && km.Value > 0;
        if (hasDistanceFilter)
        {
            if (!double.IsFinite(lat!.Value) || lat.Value is < -90 or > 90) hasDistanceFilter = false;
            if (!double.IsFinite(lng!.Value) || lng.Value is < -180 or > 180) hasDistanceFilter = false;
            if (!double.IsFinite(km!.Value) || km.Value is <= 0 or > 25_000) hasDistanceFilter = false;
        }

        // Cargar stores base.
        var stores = await db.Stores.AsNoTracking().ToListAsync(cancellationToken);

        // Pre-cargar contadores publicados (vitrina).
        var pubProductCounts = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published)
            .GroupBy(p => p.StoreId)
            .Select(g => new { StoreId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Cnt, cancellationToken);

        var pubServiceCounts = await db.StoreServices.AsNoTracking()
            .Where(s => s.Published == null || s.Published == true)
            .GroupBy(s => s.StoreId)
            .Select(g => new { StoreId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Cnt, cancellationToken);

        // Filtrar por nombre/categoría/vitrina/distancia en memoria (sin PostGIS).
        var items = new List<(StoreRow row, int pp, int ps, double? dist)>(capacity: Math.Min(take, stores.Count));
        foreach (var s in stores)
        {
            if (!string.IsNullOrEmpty(nameQ))
            {
                var hay = s.Name ?? "";
                var idx = CultureInfo.GetCultureInfo("es").CompareInfo.IndexOf(
                    hay,
                    nameQ,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (idx < 0)
                    continue;
            }

            if (!string.IsNullOrEmpty(catQ))
            {
                if (!StoreHasCategory(s, catQ))
                    continue;
            }

            pubProductCounts.TryGetValue(s.Id, out var pp);
            pubServiceCounts.TryGetValue(s.Id, out var ps);

            double? dist = null;
            if (hasDistanceFilter)
            {
                if (s.LocationLatitude is null || s.LocationLongitude is null)
                    continue;
                dist = HaversineKm(lat!.Value, lng!.Value, s.LocationLatitude.Value, s.LocationLongitude.Value);
                if (dist > km!.Value)
                    continue;
            }

            items.Add((s, pp, ps, dist));
        }

        // Orden: por distancia si aplica, si no por trustScore desc, luego nombre.
        IEnumerable<(StoreRow row, int pp, int ps, double? dist)> ordered = items;
        if (hasDistanceFilter)
            ordered = ordered.OrderBy(x => x.dist ?? double.MaxValue);
        else
            ordered = ordered.OrderByDescending(x => x.row.TrustScore).ThenBy(x => x.row.Name, StringComparer.CurrentCultureIgnoreCase);

        var outItems = ordered.Take(take).Select(x =>
        {
            var node = new JsonObject
            {
                ["id"] = x.row.Id,
                ["name"] = x.row.Name,
                ["verified"] = x.row.Verified,
                ["transportIncluded"] = x.row.TransportIncluded,
                ["trustScore"] = x.row.TrustScore,
                ["ownerUserId"] = x.row.OwnerUserId,
            };
            if (!string.IsNullOrEmpty(x.row.AvatarUrl))
                node["avatarUrl"] = x.row.AvatarUrl;

            try { node["categories"] = JsonNode.Parse(x.row.CategoriesJson) ?? new JsonArray(); }
            catch { node["categories"] = new JsonArray(); }

            if (x.row.LocationLatitude is { } la && x.row.LocationLongitude is { } lo)
            {
                node["location"] = new JsonObject { ["lat"] = la, ["lng"] = lo };
            }

            return new StoreSearchItem(node, x.pp, x.ps, x.dist);
        }).ToList();

        return Ok(new StoreSearchResponse(outItems));
    }

    private static bool StoreHasCategory(StoreRow row, string categoryQ)
    {
        try
        {
            using var doc = JsonDocument.Parse(row.CategoriesJson ?? "[]");
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var c = el.GetString() ?? "";
                if (c.Contains(categoryQ, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        static double DegToRad(double d) => d * (Math.PI / 180.0);
        var dLat = DegToRad(lat2 - lat1);
        var dLng = DegToRad(lng2 - lng1);
        var a =
            Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(DegToRad(lat1)) * Math.Cos(DegToRad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        var c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        return R * c;
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

        if (root.TryGetProperty("stores", out var storesEl) && storesEl.ValueKind == JsonValueKind.Object)
            return body;

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
