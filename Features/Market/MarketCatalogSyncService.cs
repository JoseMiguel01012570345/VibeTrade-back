using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketCatalogSyncService(AppDbContext db) : IMarketCatalogSyncService
{
    /// <summary>
    /// Imagen por defecto solo en el feed de <b>ofertas</b> (<c>imageUrl</c> / <c>imageUrls</c>).
    /// El catálogo de tienda (<c>photoUrls</c> en <see cref="ServiceToJson"/>) no incluye placeholder.
    /// Archivo estático: <c>web/public/tool.png</c>.
    /// </summary>
    private const string DefaultServiceOfferImageUrl = "/tool.png";

    public async Task ApplyStoresAndCatalogsFromWorkspaceAsync(
        JsonElement workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetStoresObject(workspaceRoot, out var storesEl))
            return;

        var now = DateTimeOffset.UtcNow;

        // No borrar tiendas que no vengan en el JSON: el PUT /workspace suele ser un snapshot parcial
        // del cliente (p. ej. solo las tiendas del usuario), no el catálogo global de la plataforma.

        JsonElement catalogs = default;
        var hasCatalogs = workspaceRoot.TryGetProperty("storeCatalogs", out catalogs)
                          && catalogs.ValueKind == JsonValueKind.Object;

        foreach (var prop in storesEl.EnumerateObject())
        {
            var storeId = prop.Name;
            var el = prop.Value;
            var ownerUserId = el.TryGetProperty("ownerUserId", out var ou) && ou.ValueKind == JsonValueKind.String
                ? ou.GetString()!
                : "unknown";

            await EnsureUserExistsAsync(ownerUserId, now, cancellationToken);

            var row = await db.Stores.FindAsync([storeId], cancellationToken);
            if (row is null)
            {
                row = new StoreRow
                {
                    Id = storeId,
                    CreatedAt = now,
                    JoinedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                db.Stores.Add(row);
            }

            row.OwnerUserId = ownerUserId;
            row.Name = GetString(el, "name") ?? row.Name;
            row.NormalizedName = NormalizeStoreName(row.Name);
            row.Verified = el.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
            row.TransportIncluded = el.TryGetProperty("transportIncluded", out var t) &&
                                    t.ValueKind == JsonValueKind.True;
            row.TrustScore = el.TryGetProperty("trustScore", out var ts) && ts.TryGetInt32(out var ti) ? ti : row.TrustScore;
            row.AvatarUrl = GetString(el, "avatarUrl");
            row.CategoriesJson = SerializeStringArray(el, "categories");
            row.UpdatedAt = now;
            ApplyLocationFromWorkspace(el, row);

            if (hasCatalogs && catalogs.TryGetProperty(storeId, out var catEl) && catEl.ValueKind == JsonValueKind.Object)
            {
                row.Pitch = GetString(catEl, "pitch") ?? "";
                row.JoinedAtMs = catEl.TryGetProperty("joinedAt", out var ja) && ja.TryGetInt64(out var jn) ? jn : row.JoinedAtMs;
                await SyncProductsAsync(storeId, catEl, now, cancellationToken);
                await SyncServicesAsync(storeId, catEl, now, cancellationToken);
            }
        }

        ApplyOfferQaFromWorkspace(workspaceRoot, now);

        EnsureNoDuplicateStoreNames();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateStoreNameException(null);
        }
    }

    /// <summary>Misma regla que <c>normStoreName</c> en el cliente (espacios colapsados, trim, minúsculas).</summary>
    private static string? NormalizeStoreName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var collapsed = Regex.Replace(name.Trim(), @"\s+", " ");
        if (collapsed.Length == 0)
            return null;
        return collapsed.ToLowerInvariant();
    }

    private void EnsureNoDuplicateStoreNames()
    {
        var tracked = db.ChangeTracker.Entries<StoreRow>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.NormalizedName is not null)
            .Select(e => e.Entity)
            .ToList();
        var dup = tracked
            .GroupBy(x => x.NormalizedName!, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new DuplicateStoreNameException(dup.Key);
    }

    private static void ApplyLocationFromWorkspace(JsonElement storeEl, StoreRow row)
    {
        row.LocationLatitude = null;
        row.LocationLongitude = null;
        if (!storeEl.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Object)
            return;
        if (!loc.TryGetProperty("lat", out var latEl) || !latEl.TryGetDouble(out var lat))
            return;
        if (!loc.TryGetProperty("lng", out var lngEl) || !lngEl.TryGetDouble(out var lng))
            return;
        if (!double.IsFinite(lat) || lat < -90 || lat > 90)
            return;
        if (!double.IsFinite(lng) || lng < -180 || lng > 180)
            return;
        row.LocationLatitude = lat;
        row.LocationLongitude = lng;
    }

    private static bool TryGetStoresObject(JsonElement workspaceRoot, out JsonElement storesEl)
    {
        storesEl = default;
        if (!workspaceRoot.TryGetProperty("stores", out var s) || s.ValueKind != JsonValueKind.Object)
            return false;
        storesEl = s;
        return true;
    }

    private async Task EnsureUserExistsAsync(string userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.UserAccounts.Local.Any(u => u.Id == userId))
            return;
        if (await db.UserAccounts.AnyAsync(u => u.Id == userId, cancellationToken))
            return;
        db.UserAccounts.Add(new UserAccount
        {
            Id = userId,
            DisplayName = "Usuario",
            TrustScore = 75,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private async Task SyncProductsAsync(
        string storeId,
        JsonElement catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!catalogEl.TryGetProperty("products", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var incomingIds = new HashSet<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                incomingIds.Add(idEl.GetString()!);
        }

        var stale = await db.StoreProducts
            .Where(p => p.StoreId == storeId && !incomingIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        db.StoreProducts.RemoveRange(stale);

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = GetString(item, "id");
            if (string.IsNullOrEmpty(id))
                continue;

            ThrowIfProductCurrencyInvalid(item, id);

            var row = await db.StoreProducts.FindAsync([id], cancellationToken);
            if (row is null)
            {
                row = new StoreProductRow { Id = id, StoreId = storeId };
                db.StoreProducts.Add(row);
            }

            row.Category = GetString(item, "category") ?? "";
            row.Name = GetString(item, "name") ?? "";
            row.Model = GetString(item, "model");
            row.ShortDescription = GetString(item, "shortDescription") ?? "";
            row.MainBenefit = GetString(item, "mainBenefit") ?? "";
            row.TechnicalSpecs = GetString(item, "technicalSpecs") ?? "";
            row.Condition = GetString(item, "condition") ?? "";
            row.Price = GetString(item, "price") ?? "";
            row.MonedaPrecio = GetString(item, "monedaPrecio");
            row.MonedasJson = SerializeMonedasFromCatalogItemJson(item);
            row.TaxesShippingInstall = GetString(item, "taxesShippingInstall");
            row.Availability = GetString(item, "availability") ?? "";
            row.WarrantyReturn = GetString(item, "warrantyReturn") ?? "";
            row.ContentIncluded = GetString(item, "contentIncluded") ?? "";
            row.UsageConditions = GetString(item, "usageConditions") ?? "";
            row.Published = item.TryGetProperty("published", out var pub) && pub.ValueKind == JsonValueKind.True;
            row.PhotoUrlsJson = SerializeJsonElement(item, "photoUrls") ?? "[]";
            row.CustomFieldsJson = SerializeJsonElement(item, "customFields") ?? "[]";
            row.UpdatedAt = now;
        }
    }

    private async Task SyncServicesAsync(
        string storeId,
        JsonElement catalogEl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!catalogEl.TryGetProperty("services", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var incomingIds = new HashSet<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                incomingIds.Add(idEl.GetString()!);
        }

        var stale = await db.StoreServices
            .Where(s => s.StoreId == storeId && !incomingIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        db.StoreServices.RemoveRange(stale);

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = GetString(item, "id");
            if (string.IsNullOrEmpty(id))
                continue;

            ThrowIfServiceCurrencyInvalid(item, id);

            var row = await db.StoreServices.FindAsync([id], cancellationToken);
            if (row is null)
            {
                row = new StoreServiceRow { Id = id, StoreId = storeId };
                db.StoreServices.Add(row);
            }

            row.Published = item.TryGetProperty("published", out var p)
                ? p.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                }
                : null;
            row.Category = GetString(item, "category") ?? "";
            row.TipoServicio = GetString(item, "tipoServicio") ?? "";
            row.Descripcion = GetString(item, "descripcion") ?? "";
            row.RiesgosJson = SerializeJsonElement(item, "riesgos") ?? row.RiesgosJson;
            row.Incluye = GetString(item, "incluye") ?? "";
            row.NoIncluye = GetString(item, "noIncluye") ?? "";
            row.DependenciasJson = SerializeJsonElement(item, "dependencias") ?? row.DependenciasJson;
            row.Entregables = GetString(item, "entregables") ?? "";
            row.GarantiasJson = SerializeJsonElement(item, "garantias") ?? row.GarantiasJson;
            row.PropIntelectual = GetString(item, "propIntelectual") ?? "";
            row.MonedasJson = SerializeMonedasFromCatalogItemJson(item);
            row.CustomFieldsJson = SerializeJsonElement(item, "customFields") ?? "[]";
            if (item.TryGetProperty("photoUrls", out var phEl) && phEl.ValueKind == JsonValueKind.Array)
                row.PhotoUrlsJson = await FilterIncomingServicePhotoUrlsToImageMediaJsonAsync(phEl, cancellationToken);
            row.UpdatedAt = now;
        }
    }

    /// <summary>
    /// <c>photoUrls</c> de servicios: solo imágenes (los adjuntos PDF u otros van en <c>customFields</c>).
    /// Resuelve <c>/api/v1/media/{id}</c> contra <see cref="StoredMediaRow.MimeType"/>.
    /// </summary>
    private async Task<string> FilterIncomingServicePhotoUrlsToImageMediaJsonAsync(
        JsonElement photoUrlsArray,
        CancellationToken cancellationToken)
    {
        if (photoUrlsArray.ValueKind != JsonValueKind.Array)
            return "[]";

        var raw = new List<string>();
        foreach (var el in photoUrlsArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var u = (el.GetString() ?? "").Trim();
            if (u.Length > 0)
                raw.Add(u);
        }

        if (raw.Count == 0)
            return "[]";

        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in raw)
        {
            var id = TryGetStoredMediaIdFromCatalogUrl(u);
            if (id is not null)
                mediaIds.Add(id);
        }

        var mimeById = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mediaIds.Count > 0)
        {
            var rows = await db.StoredMedia.AsNoTracking()
                .Where(m => mediaIds.Contains(m.Id))
                .Select(m => new { m.Id, m.MimeType })
                .ToListAsync(cancellationToken);
            foreach (var r in rows)
                mimeById[r.Id] = r.MimeType ?? "";
        }

        var kept = new List<string>(raw.Count);
        foreach (var u in raw)
        {
            var id = TryGetStoredMediaIdFromCatalogUrl(u);
            if (id is not null)
            {
                if (mimeById.TryGetValue(id, out var mt) &&
                    mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    kept.Add(u);
                continue;
            }

            if (IsLikelyNonMediaCatalogImageUrl(u))
                kept.Add(u);
        }

        return JsonSerializer.Serialize(kept);
    }

    private static string? TryGetStoredMediaIdFromCatalogUrl(string url)
    {
        var u = (url ?? "").Trim();
        if (u.Length == 0)
            return null;
        const string prefix = "/api/v1/media/";
        var idx = u.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var rest = u[(idx + prefix.Length)..];
        var q = rest.IndexOf('?');
        if (q >= 0)
            rest = rest[..q];
        rest = Uri.UnescapeDataString(rest);
        return string.IsNullOrEmpty(rest) ? null : rest;
    }

    /// <summary>URLs que no pasan por <c>StoredMedia</c> pero son claramente imagen (no PDF).</summary>
    private static bool IsLikelyNonMediaCatalogImageUrl(string u)
    {
        if (u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        var lower = u.ToLowerInvariant();
        if (lower.Contains(".pdf") || lower.Contains("application/pdf"))
            return false;
        if (lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".png") ||
            lower.Contains(".gif") || lower.Contains(".webp") || lower.Contains(".avif") ||
            lower.Contains(".svg"))
            return true;
        return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Persiste <c>offers[*].qa</c> en filas de producto/servicio (mismo id que la oferta).</summary>
    private void ApplyOfferQaFromWorkspace(JsonElement workspaceRoot, DateTimeOffset now)
    {
        if (!workspaceRoot.TryGetProperty("offers", out var offersEl) || offersEl.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in offersEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!prop.Value.TryGetProperty("qa", out var qaEl) || qaEl.ValueKind != JsonValueKind.Array)
                continue;

            var qaRaw = qaEl.GetRawText();
            var id = prop.Name;

            var product = db.StoreProducts.Find(id);
            if (product is not null)
            {
                product.OfferQaJson = qaRaw;
                product.UpdatedAt = now;
                continue;
            }

            var service = db.StoreServices.Find(id);
            if (service is not null)
            {
                service.OfferQaJson = qaRaw;
                service.UpdatedAt = now;
            }
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static void ThrowIfProductCurrencyInvalid(JsonElement item, string id)
    {
        if (string.IsNullOrWhiteSpace(GetString(item, "monedaPrecio")))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": la moneda del precio es obligatoria.");
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(item))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    private static void ThrowIfServiceCurrencyInvalid(JsonElement item, string id)
    {
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(item))
            throw new CatalogCurrencyValidationException(
                $"Servicio \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    /// <summary>Al menos un código en <c>monedas</c> no vacío, o <c>moneda</c> legado.</summary>
    private static bool CatalogItemHasAtLeastOneAcceptedMoneda(JsonElement item)
    {
        if (item.TryGetProperty("monedas", out var m) && m.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in m.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                    return true;
            }
        }

        return !string.IsNullOrWhiteSpace(GetString(item, "moneda"));
    }

    /// <summary>Serializa <c>monedas</c> o, si falta, un único <c>moneda</c> legado (productos y servicios).</summary>
    private static string SerializeMonedasFromCatalogItemJson(JsonElement item)
    {
        if (item.TryGetProperty("monedas", out var m) && m.ValueKind == JsonValueKind.Array)
            return m.GetRawText();
        var legacy = GetString(item, "moneda");
        if (!string.IsNullOrEmpty(legacy))
            return JsonSerializer.Serialize(new[] { legacy });
        return "[]";
    }

    private static string SerializeStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return "[]";
        if (p.ValueKind != JsonValueKind.Array)
            return "[]";
        return p.GetRawText();
    }

    private static string? SerializeJsonElement(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var p))
            return null;
        return p.GetRawText();
    }

    public async Task<JsonObject> BuildStoresJsonObjectAsync(CancellationToken cancellationToken = default)
    {
        var o = new JsonObject();
        var list = await db.Stores.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var s in list)
        {
            var node = new JsonObject
            {
                ["id"] = s.Id,
                ["name"] = s.Name,
                ["verified"] = s.Verified,
                ["transportIncluded"] = s.TransportIncluded,
                ["trustScore"] = s.TrustScore,
                ["ownerUserId"] = s.OwnerUserId,
            };
            if (!string.IsNullOrEmpty(s.AvatarUrl))
                node["avatarUrl"] = s.AvatarUrl;
            try
            {
                node["categories"] = JsonNode.Parse(s.CategoriesJson) ?? new JsonArray();
            }
            catch
            {
                node["categories"] = new JsonArray();
            }

            if (s.LocationLatitude is { } la && s.LocationLongitude is { } lo)
            {
                node["location"] = new JsonObject
                {
                    ["lat"] = la,
                    ["lng"] = lo,
                };
            }

            o[s.Id] = node;
        }

        return o;
    }

    public async Task<JsonObject> BuildStoreCatalogsJsonObjectAsync(CancellationToken cancellationToken = default)
    {
        var root = new JsonObject();
        var storeIds = await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken);
        foreach (var storeId in storeIds)
        {
            var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == storeId, cancellationToken);
            var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

            var cat = new JsonObject
            {
                ["pitch"] = store.Pitch,
                ["joinedAt"] = store.JoinedAtMs,
                ["products"] = new JsonArray(products.Select(ProductToJson).ToArray<JsonNode?>()),
                ["services"] = new JsonArray(services.Select(ServiceToJson).ToArray<JsonNode?>()),
            };
            root[storeId] = cat;
        }

        return root;
    }

    private static JsonObject ProductToJson(StoreProductRow p)
    {
        var o = new JsonObject
        {
            ["id"] = p.Id,
            ["storeId"] = p.StoreId,
            ["category"] = p.Category,
            ["name"] = p.Name,
            ["shortDescription"] = p.ShortDescription,
            ["mainBenefit"] = p.MainBenefit,
            ["technicalSpecs"] = p.TechnicalSpecs,
            ["condition"] = p.Condition,
            ["price"] = p.Price,
            ["availability"] = p.Availability,
            ["warrantyReturn"] = p.WarrantyReturn,
            ["contentIncluded"] = p.ContentIncluded,
            ["usageConditions"] = p.UsageConditions,
            ["published"] = p.Published,
        };
        if (!string.IsNullOrEmpty(p.Model))
            o["model"] = p.Model;
        if (!string.IsNullOrEmpty(p.MonedaPrecio))
            o["monedaPrecio"] = p.MonedaPrecio;
        try
        {
            o["monedas"] = JsonNode.Parse(p.MonedasJson) ?? new JsonArray();
        }
        catch
        {
            o["monedas"] = new JsonArray();
        }

        if (!string.IsNullOrEmpty(p.TaxesShippingInstall))
            o["taxesShippingInstall"] = p.TaxesShippingInstall;
        try
        {
            o["photoUrls"] = JsonNode.Parse(p.PhotoUrlsJson) ?? new JsonArray();
        }
        catch
        {
            o["photoUrls"] = new JsonArray();
        }

        try
        {
            o["customFields"] = JsonNode.Parse(p.CustomFieldsJson) ?? new JsonArray();
        }
        catch
        {
            o["customFields"] = new JsonArray();
        }

        return o;
    }

    private static JsonObject ServiceToJson(StoreServiceRow s)
    {
        var o = new JsonObject
        {
            ["id"] = s.Id,
            ["storeId"] = s.StoreId,
            ["category"] = s.Category,
            ["tipoServicio"] = s.TipoServicio,
            ["descripcion"] = s.Descripcion,
            ["incluye"] = s.Incluye,
            ["noIncluye"] = s.NoIncluye,
            ["entregables"] = s.Entregables,
            ["propIntelectual"] = s.PropIntelectual,
        };
        if (s.Published.HasValue)
            o["published"] = s.Published.Value;
        try
        {
            o["riesgos"] = JsonNode.Parse(s.RiesgosJson) ?? new JsonObject();
        }
        catch
        {
            o["riesgos"] = new JsonObject();
        }

        try
        {
            o["dependencias"] = JsonNode.Parse(s.DependenciasJson) ?? new JsonObject();
        }
        catch
        {
            o["dependencias"] = new JsonObject();
        }

        try
        {
            o["garantias"] = JsonNode.Parse(s.GarantiasJson) ?? new JsonObject();
        }
        catch
        {
            o["garantias"] = new JsonObject();
        }

        try
        {
            o["monedas"] = JsonNode.Parse(s.MonedasJson) ?? new JsonArray();
        }
        catch
        {
            o["monedas"] = new JsonArray();
        }

        try
        {
            o["customFields"] = JsonNode.Parse(s.CustomFieldsJson) ?? new JsonArray();
        }
        catch
        {
            o["customFields"] = new JsonArray();
        }

        var svcPhotoUrls = CollectDisplayablePhotoUrls(s.PhotoUrlsJson);
        // Sin fotos reales: array vacío. El placeholder `/tool.png` solo se aplica en ofertas (ServiceRowToOfferJson).
        Console.WriteLine($"PhotoUrlsJson: {s.PhotoUrlsJson}");
        o["photoUrls"] = svcPhotoUrls.Count > 0
            ? new JsonArray(svcPhotoUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray())
            : new JsonArray();

        return o;
    }

    public async Task<JsonDocument?> GetStoreDetailDocumentAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (store is null)
            return null;

        var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

        var storeNode = new JsonObject
        {
            ["id"] = store.Id,
            ["name"] = store.Name,
            ["verified"] = store.Verified,
            ["transportIncluded"] = store.TransportIncluded,
            ["trustScore"] = store.TrustScore,
            ["ownerUserId"] = store.OwnerUserId,
        };
        if (!string.IsNullOrEmpty(store.AvatarUrl))
            storeNode["avatarUrl"] = store.AvatarUrl;
        try
        {
            storeNode["categories"] = JsonNode.Parse(store.CategoriesJson) ?? new JsonArray();
        }
        catch
        {
            storeNode["categories"] = new JsonArray();
        }

        if (store.LocationLatitude is { } lat && store.LocationLongitude is { } lng)
        {
            storeNode["location"] = new JsonObject
            {
                ["lat"] = lat,
                ["lng"] = lng,
            };
        }

        var catalog = new JsonObject
        {
            ["pitch"] = store.Pitch,
            ["joinedAt"] = store.JoinedAtMs,
            ["products"] = new JsonArray(products.Select(ProductToJson).ToArray<JsonNode?>()),
            ["services"] = new JsonArray(services.Select(ServiceToJson).ToArray<JsonNode?>()),
        };

        var root = new JsonObject { ["store"] = storeNode, ["catalog"] = catalog };
        return JsonDocument.Parse(root.ToJsonString());
    }

    public async Task<(JsonObject Offers, JsonArray OfferIds)> BuildPublishedOffersFeedAsync(
        CancellationToken cancellationToken = default)
    {
        var stores = await db.Stores.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, cancellationToken);

        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.Published)
            .ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking()
            .Where(s => s.Published == null || s.Published == true)
            .ToListAsync(cancellationToken);

        var entries = new List<(DateTimeOffset at, string id, JsonObject offer)>(
            capacity: products.Count + services.Count);

        foreach (var p in products)
        {
            if (!stores.ContainsKey(p.StoreId))
                continue;
            entries.Add((p.UpdatedAt, p.Id, ProductRowToOfferJson(p)));
        }

        foreach (var s in services)
        {
            if (!stores.ContainsKey(s.StoreId))
                continue;
            entries.Add((s.UpdatedAt, s.Id, ServiceRowToOfferJson(s)));
        }

        entries.Sort((a, b) => b.at.CompareTo(a.at));

        var offersObj = new JsonObject();
        var ids = new JsonArray();
        foreach (var (_, id, offer) in entries)
        {
            offersObj[id] = offer;
            ids.Add(id);
        }

        return (offersObj, ids);
    }

    private JsonObject ProductRowToOfferJson(StoreProductRow p)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Category))
            tags.Add(p.Category.Trim());
        if (!string.IsNullOrWhiteSpace(p.Condition))
            tags.Add(p.Condition.Trim());
        tags.Add("Producto");

        var price = FormatProductPrice(p);
        var title = string.IsNullOrWhiteSpace(p.Name) ? "Producto" : p.Name.Trim();
        var photoUrls = CollectDisplayablePhotoUrls(p.PhotoUrlsJson);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : null;

        return new JsonObject
        {
            ["id"] = p.Id,
            ["storeId"] = p.StoreId,
            ["title"] = title,
            ["price"] = price,
            ["description"] = OfferDescriptionForProduct(p),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["imageUrl"] = primary,
            ["imageUrls"] = new JsonArray(photoUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray()),
            ["qa"] = ParseOfferQaJsonNode(p.OfferQaJson),
        };
    }

    private JsonObject ServiceRowToOfferJson(StoreServiceRow s)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Category))
            tags.Add(s.Category.Trim());
        if (!string.IsNullOrWhiteSpace(s.TipoServicio))
            tags.Add(s.TipoServicio.Trim());
        tags.Add("Servicio");

        var title = !string.IsNullOrWhiteSpace(s.TipoServicio)
            ? s.TipoServicio.Trim()
            : (!string.IsNullOrWhiteSpace(s.Category) ? s.Category.Trim() : "Servicio");
        var photoUrls = CollectServiceOfferGalleryUrls(s);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : DefaultServiceOfferImageUrl;
        var imageUrlsNode = photoUrls.Count > 0
            ? new JsonArray(photoUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray())
            : new JsonArray((JsonNode?)JsonValue.Create(DefaultServiceOfferImageUrl));

        return new JsonObject
        {
            ["id"] = s.Id,
            ["storeId"] = s.StoreId,
            ["title"] = title,
            ["price"] = FormatServicePriceLine(s),
            ["description"] = OfferDescriptionForService(s),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["imageUrl"] = primary,
            ["imageUrls"] = imageUrlsNode,
            ["qa"] = ParseOfferQaJsonNode(s.OfferQaJson),
        };
    }

    private static string OfferDescriptionForProduct(StoreProductRow p)
    {
        var a = (p.ShortDescription ?? "").Trim();
        if (a.Length > 0)
            return a;
        var b = (p.MainBenefit ?? "").Trim();
        return b.Length > 0 ? b : "";
    }

    private static string OfferDescriptionForService(StoreServiceRow s) =>
        (s.Descripcion ?? "").Trim();

    private static string FormatProductPrice(StoreProductRow p)
    {
        var price = (p.Price ?? "").Trim();
        var mon = (p.MonedaPrecio ?? "").Trim();
        return $"{price} {mon}";
    }

    private static string? FormatServicePriceLine(StoreServiceRow s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s.MonedasJson ?? "[]");
            var codes = doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => (e.GetString() ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return codes.Count == 0 ? null : string.Join(" · ", codes);
        }
        catch
        {
            return "Consultar";
        }
    }

    /// <summary>
    /// Galería de oferta de servicio: <see cref="StoreServiceRow.PhotoUrlsJson"/> más adjuntos <c>kind: image</c> en <see cref="StoreServiceRow.CustomFieldsJson"/>.
    /// </summary>
    private static List<string> CollectServiceOfferGalleryUrls(StoreServiceRow s)
    {
        var list = CollectDisplayablePhotoUrls(s.PhotoUrlsJson);
        var seen = new HashSet<string>(list, StringComparer.Ordinal);
        AppendDisplayableImageUrlsFromCustomFieldsJson(s.CustomFieldsJson, list, seen);
        return list;
    }

    private static List<string> CollectDisplayablePhotoUrls(string photoUrlsJson)
    {
        var list = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(photoUrlsJson ?? "[]");
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                    continue;
                var u = (el.GetString() ?? "").Trim();
                if (u.Length == 0 || !IsDisplayableCatalogImageUrl(u))
                    continue;
                list.Add(u);
            }
        }
        catch
        {
            /* ignore */
        }

        return list;
    }

    private static void AppendDisplayableImageUrlsFromCustomFieldsJson(string? customFieldsJson, List<string> list, HashSet<string> seen)
    {
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson ?? "[]");
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return;
            foreach (var field in doc.RootElement.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                    continue;
                if (!TryGetPropertyIgnoreCase(field, "attachments", out var atts) ||
                    atts.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var att in atts.EnumerateArray())
                {
                    if (att.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!TryGetPropertyIgnoreCase(att, "kind", out var kindEl) ||
                        kindEl.ValueKind != JsonValueKind.String)
                        continue;
                    if (!string.Equals(kindEl.GetString(), "image", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!TryGetPropertyIgnoreCase(att, "url", out var urlEl) ||
                        urlEl.ValueKind != JsonValueKind.String)
                        continue;
                    var u = (urlEl.GetString() ?? "").Trim();
                    if (u.Length == 0 || !IsDisplayableCatalogImageUrl(u) || !seen.Add(u))
                        continue;
                    list.Add(u);
                }
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsDisplayableCatalogImageUrl(string u) =>
        u.StartsWith("/api/v1/media/", StringComparison.Ordinal) ||
        u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
        IsRootRelativeStaticImageUrl(u);

    /// <summary>Rutas bajo el origen del SPA (p. ej. <c>/tool.png</c> en <c>public/</c>), no API.</summary>
    private static bool IsRootRelativeStaticImageUrl(string u)
    {
        if (!u.StartsWith("/", StringComparison.Ordinal) ||
            u.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return false;
        var lower = u.ToLowerInvariant();
        var q = lower.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            lower = lower[..q];
        return lower.EndsWith(".png", StringComparison.Ordinal) ||
               lower.EndsWith(".jpg", StringComparison.Ordinal) ||
               lower.EndsWith(".jpeg", StringComparison.Ordinal) ||
               lower.EndsWith(".gif", StringComparison.Ordinal) ||
               lower.EndsWith(".webp", StringComparison.Ordinal) ||
               lower.EndsWith(".avif", StringComparison.Ordinal) ||
               lower.EndsWith(".svg", StringComparison.Ordinal);
    }

    private static JsonArray ParseOfferQaJsonNode(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JsonArray();
            var n = JsonNode.Parse(json);
            return n as JsonArray ?? new JsonArray();
        }
        catch
        {
            return new JsonArray();
        }
    }
}
