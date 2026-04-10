using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Api;

/// <summary>Persistencia del workspace de mercado (tiendas, ofertas, hilos, rutas públicas).</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class MarketController(IMarketWorkspaceService marketWorkspace, AppDbContext db) : ControllerBase
{
    public sealed record CatalogCategoriesResponse(IReadOnlyList<string> Categories);

    public sealed record CurrenciesResponse(IReadOnlyList<string> Currencies);

    public sealed record StoreDetailBody(string? ViewerUserId, string? ViewerRole);

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

    /// <summary>Reemplaza el snapshot del mercado (misma forma que el store Zustand del frontend).</summary>
    /// <param name="body">JSON con stores, offers, offerIds, storeCatalogs, threads, routeOfferPublic.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPut("workspace")]
    [RequestSizeLimit(524_288_000L)] // 500 MiB; alinear con Kestrel en Program.cs
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PutWorkspace([FromBody] JsonDocument body, CancellationToken cancellationToken)
    {
        try
        {
            await marketWorkspace.SaveAsync(body, cancellationToken);
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
}
