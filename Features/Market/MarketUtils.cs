using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Workspace;
using VibeTrade.Backend.Features.Market.Catalog;

namespace VibeTrade.Backend.Features.Market;

internal static class MarketCatalogConstants
{
    public const string DefaultServiceOfferImageUrl = "/tool.png";
    public const string MediaApiPrefix = "/api/v1/media/";
}

internal static class MarketCatalogCurrency
{
    public static void ThrowIfProductCurrencyInvalid(StoreProductPutRequest p, string id)
    {
        if (string.IsNullOrWhiteSpace(p.MonedaPrecio))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": la moneda del precio es obligatoria.");
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(p))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": indica al menos una moneda aceptada para el pago.");
    }

    public static void ThrowIfServiceCurrencyInvalid(StoreServicePutRequest s, string id)
    {
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(s))
            throw new CatalogCurrencyValidationException(
                $"Servicio \"{id}\": indica al menos una moneda aceptada para el pago.");
    }

    public static List<string> BuildMonedasList(StoreProductPutRequest p) =>
        BuildMonedasList(p.Monedas, p.Moneda);

    public static List<string> BuildMonedasList(StoreServicePutRequest s) =>
        BuildMonedasList(s.Monedas, s.Moneda);

    public static List<string> BuildMonedasList(IReadOnlyList<string>? monedas, string? moneda)
    {
        if (monedas is { Count: > 0 })
        {
            var withAny = new List<string>(monedas.Count);
            foreach (var c in monedas)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    withAny.Add(c.Trim());
            }

            if (withAny.Count > 0)
                return withAny;
        }

        if (!string.IsNullOrWhiteSpace(moneda))
            return new List<string> { moneda.Trim() };

        return new List<string>();
    }

    public static bool CatalogItemHasAtLeastOneAcceptedMoneda(StoreProductPutRequest p) =>
        CatalogItemHasAtLeastOneAcceptedMoneda(p.Monedas, p.Moneda);

    public static bool CatalogItemHasAtLeastOneAcceptedMoneda(StoreServicePutRequest s) =>
        CatalogItemHasAtLeastOneAcceptedMoneda(s.Monedas, s.Moneda);

    private static bool CatalogItemHasAtLeastOneAcceptedMoneda(IReadOnlyList<string>? monedas, string? moneda)
    {
        if (monedas is { Count: > 0 })
        {
            foreach (var c in monedas)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    return true;
            }
        }

        return !string.IsNullOrWhiteSpace(moneda);
    }
}

internal static class MarketCatalogIncomingServicePhotos
{
    public static async Task<List<string>> FilterToStoredImageListAsync(
        AppDbContext db,
        IReadOnlyList<string>? photoUrls,
        CancellationToken cancellationToken)
    {
        if (photoUrls is not { Count: > 0 })
            return new List<string>();

        var raw = new List<string>(photoUrls.Count);
        foreach (var u0 in photoUrls)
        {
            var u = (u0 ?? "").Trim();
            if (u.Length > 0) raw.Add(u);
        }

        if (raw.Count == 0)
            return new List<string>();

        return await FilterRawUrlListToStoredImageListAsync(db, raw, cancellationToken);
    }

    private static async Task<List<string>> FilterRawUrlListToStoredImageListAsync(
        AppDbContext db,
        List<string> raw,
        CancellationToken cancellationToken)
    {
        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in raw)
        {
            var id = MarketCatalogPhotoRules.TryGetStoredMediaIdFromCatalogUrl(u);
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
            var id = MarketCatalogPhotoRules.TryGetStoredMediaIdFromCatalogUrl(u);
            if (id is not null)
            {
                if (mimeById.TryGetValue(id, out var mt) &&
                    mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    kept.Add(u);
                continue;
            }

            if (MarketCatalogPhotoRules.IsLikelyNonMediaCatalogImageUrl(u))
                kept.Add(u);
        }

        return kept;
    }
}

internal static class MarketCatalogPhotoRules
{
    public static string? TryGetStoredMediaIdFromCatalogUrl(string url)
    {
        var u = (url ?? "").Trim();
        if (u.Length == 0)
            return null;
        var prefix = MarketCatalogConstants.MediaApiPrefix;
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

    public static bool IsLikelyNonMediaCatalogImageUrl(string u)
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

    public static bool IsDisplayableCatalogImageUrl(string u) =>
        u.StartsWith(MarketCatalogConstants.MediaApiPrefix, StringComparison.Ordinal) ||
        u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
        IsRootRelativeStaticImageUrl(u);

    public static bool IsRootRelativeStaticImageUrl(string u)
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

    public static List<string> CollectDisplayablePhotoUrls(IReadOnlyList<string>? photoUrls)
    {
        var list = new List<string>();
        if (photoUrls is not { Count: > 0 })
            return list;
        try
        {
            foreach (var u0 in photoUrls)
            {
                var u = (u0 ?? "").Trim();
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

    public static void AppendDisplayableImageUrlsFromCustomFields(
        IReadOnlyList<StoreCustomFieldBody>? customFields,
        List<string> list,
        HashSet<string> seen)
    {
        try
        {
            if (customFields is not { Count: > 0 })
                return;
            foreach (var field in customFields)
            {
                if (field.Attachments is not { Count: > 0 } atts)
                    continue;
                foreach (var att in atts)
                {
                    if (!string.Equals(att.Kind, "image", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var u = (att.Url ?? "").Trim();
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

    public static List<string> CollectServiceOfferGalleryUrls(StoreServiceRow s)
    {
        var list = CollectDisplayablePhotoUrls(s.PhotoUrls);
        var seen = new HashSet<string>(list, StringComparer.Ordinal);
        AppendDisplayableImageUrlsFromCustomFields(s.CustomFields, list, seen);
        return list;
    }
}

internal static class MarketCatalogProductRowMapper
{
    public static void Apply(StoreProductPutRequest p, StoreProductRow row, DateTimeOffset now)
    {
        row.Category = p.Category ?? "";
        row.Name = p.Name ?? "";
        row.Model = p.Model;
        row.ShortDescription = p.ShortDescription ?? "";
        row.MainBenefit = p.MainBenefit ?? "";
        row.TechnicalSpecs = p.TechnicalSpecs ?? "";
        row.Condition = p.Condition ?? "";
        row.Price = p.Price ?? "";
        row.MonedaPrecio = p.MonedaPrecio;
        row.Monedas = MarketCatalogCurrency.BuildMonedasList(p);
        row.TaxesShippingInstall = p.TaxesShippingInstall;
        row.TransportIncluded = p.TransportIncluded == true;
        row.Availability = p.Availability ?? "";
        row.WarrantyReturn = p.WarrantyReturn ?? "";
        row.ContentIncluded = p.ContentIncluded ?? "";
        row.UsageConditions = p.UsageConditions ?? "";
        row.Published = p.Published == true;
        row.PhotoUrls = p.PhotoUrls is { Count: > 0 } ? p.PhotoUrls.ToList() : new List<string>();
        row.CustomFields = p.CustomFields is not null
            ? p.CustomFields.ToList()
            : row.CustomFields;
        row.UpdatedAt = now;
    }
}

internal static class MarketCatalogServiceRowMapper
{
    public static void Apply(StoreServicePutRequest s, StoreServiceRow row, DateTimeOffset now)
    {
        row.Published = s.Published;
        row.Category = s.Category ?? "";
        row.TipoServicio = s.TipoServicio ?? "";
        row.Descripcion = s.Descripcion ?? "";
        if (s.Riesgos is not null)
            row.Riesgos = s.Riesgos;
        row.Incluye = s.Incluye ?? "";
        row.NoIncluye = s.NoIncluye ?? "";
        if (s.Dependencias is not null)
            row.Dependencias = s.Dependencias;
        row.Entregables = s.Entregables ?? "";
        if (s.Garantias is not null)
            row.Garantias = s.Garantias;
        row.PropIntelectual = s.PropIntelectual ?? "";
        row.Monedas = MarketCatalogCurrency.BuildMonedasList(s);
        row.CustomFields = s.CustomFields is not null
            ? s.CustomFields.ToList()
            : row.CustomFields;
        row.UpdatedAt = now;
    }
}

internal static class MarketCatalogStoreDuplicateGuard
{
    public static void ThrowIfDuplicateNormalizedNames(AppDbContext db)
    {
        var tracked = db.ChangeTracker.Entries<StoreRow>()
            .Where(e =>
                e.State != EntityState.Deleted
                && e.Entity.NormalizedName is not null
                && e.Entity.DeletedAtUtc is null)
            .Select(e => e.Entity)
            .ToList();
        var dup = tracked
            .GroupBy(x => x.NormalizedName!, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new DuplicateStoreNameException(dup.Key);
    }
}

internal static class MarketCatalogTransportServiceRules
{
    private static readonly Regex TransportTaxonomy = new(
        @"transportista|log[ií]stica|logistica|transporte|flete|fulfillment|cadena|env[ií]o|envio|última milla|ultima milla",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceTransportHint = new(
        @"transporte|log[ií]stica|logistica|flete|transport|cadena|fulfillment|última milla|ultima milla|picking|env[ií]o|almacenaje",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool QualifiesAsTransport(string? category, string? tipoServicio)
    {
        var cat = (category ?? "").Trim();
        var tipo = (tipoServicio ?? "").Trim();
        if (cat.Length > 0 && TransportTaxonomy.IsMatch(cat)) return true;
        if (tipo.Length > 0 && ServiceTransportHint.IsMatch(tipo)) return true;
        if (cat.Length > 0 && ServiceTransportHint.IsMatch(cat)) return true;
        return false;
    }

    public static bool HasAtLeastOnePhoto(IReadOnlyList<string>? photoUrls) =>
        photoUrls is { Count: > 0 } && photoUrls.Any(s => !string.IsNullOrWhiteSpace(s));
}

internal static class MarketStoreNameNormalizer
{
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var collapsed = Regex.Replace(name.Trim(), @"\s+", " ");
        if (collapsed.Length == 0)
            return null;
        return collapsed.ToLowerInvariant();
    }
}

internal static class MarketStoreRowWorkspaceMapper
{
    public static void ApplyFields(StoreProfileWorkspaceData el, StoreRow row, DateTimeOffset now)
    {
        var d = el;
        var ownerUserId = string.IsNullOrWhiteSpace(d.OwnerUserId) ? "unknown" : d.OwnerUserId.Trim();
        row.OwnerUserId = ownerUserId;
        row.Name = d.Name?.Trim() ?? row.Name;
        row.NormalizedName = MarketStoreNameNormalizer.Normalize(row.Name);
        row.Verified = d.Verified == true;
        row.TransportIncluded = d.TransportIncluded == true;
        row.TrustScore = d.TrustScore ?? row.TrustScore;
        row.AvatarUrl = d.AvatarUrl;
        row.Categories = d.Categories is { Count: > 0 } cats
            ? cats.ToList()
            : new List<string>();
        row.UpdatedAt = now;
        if (d.Pitch is { } p)
            row.Pitch = p.Trim();
        row.WebsiteUrl = MarketWebsiteUrlNormalizer.TryNormalize(d.WebsiteUrl);
        ApplyLocation(d, row);
    }

    private static void ApplyLocation(StoreProfileWorkspaceData d, StoreRow row)
    {
        row.LocationLatitude = null;
        row.LocationLongitude = null;
        var loc = d.Location;
        if (loc is null) return;
        if (!double.IsFinite(loc.Lat) || loc.Lat < -90 || loc.Lat > 90)
            return;
        if (!double.IsFinite(loc.Lng) || loc.Lng < -180 || loc.Lng > 180)
            return;
        row.LocationLatitude = loc.Lat;
        row.LocationLongitude = loc.Lng;
    }
}

internal static class MarketWebsiteUrlNormalizer
{
    private const int MaxLen = 2048;

    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (t.Length > MaxLen)
            return null;

        if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            t = "https://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (string.IsNullOrEmpty(uri.Host))
            return null;

        return uri.AbsoluteUri;
    }
}
