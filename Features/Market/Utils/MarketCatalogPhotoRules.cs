using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;

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
