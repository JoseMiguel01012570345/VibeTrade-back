using System.Text.Json;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

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

    public static List<string> CollectDisplayablePhotoUrls(string photoUrlsJson)
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

    public static void AppendDisplayableImageUrlsFromCustomFieldsJson(
        string? customFieldsJson,
        List<string> list,
        HashSet<string> seen)
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
                if (!MarketCatalogJsonHelpers.TryGetPropertyIgnoreCase(field, "attachments", out var atts) ||
                    atts.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var att in atts.EnumerateArray())
                {
                    if (att.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!MarketCatalogJsonHelpers.TryGetPropertyIgnoreCase(att, "kind", out var kindEl) ||
                        kindEl.ValueKind != JsonValueKind.String)
                        continue;
                    if (!string.Equals(kindEl.GetString(), "image", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!MarketCatalogJsonHelpers.TryGetPropertyIgnoreCase(att, "url", out var urlEl) ||
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

    public static List<string> CollectServiceOfferGalleryUrls(StoreServiceRow s)
    {
        var list = CollectDisplayablePhotoUrls(s.PhotoUrlsJson);
        var seen = new HashSet<string>(list, StringComparer.Ordinal);
        AppendDisplayableImageUrlsFromCustomFieldsJson(s.CustomFieldsJson, list, seen);
        return list;
    }
}
