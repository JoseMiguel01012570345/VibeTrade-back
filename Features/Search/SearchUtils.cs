using System.Globalization;
using System.Linq;
using System.Text;
using VibeTrade.Backend.Features.Catalog;
using VibeTrade.Backend.Features.Catalog.Dtos;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Search.Catalog;

public static class CatalogSearchKinds
{
    public const string Store = "store";

    public const string Product = "product";

    public const string Service = "service";

    /// <summary>Publicación emergente (<c>emo_*</c>), p. ej. hoja de ruta publicada en chat.</summary>
    public const string Emergent = "emergent";
}

internal static class CatalogSearchIds
{
    public static string Store(string storeId) => $"store:{storeId}";

    public static string Product(string productId) => $"product:{productId}";

    public static string Service(string serviceId) => $"service:{serviceId}";

    public static string Emergent(string emergentPublicationId) => $"emergent:{emergentPublicationId}";
}

internal static class StoreSearchWildcard
{
    /// <summary>Escapa * ? \ para consultas wildcard de Elasticsearch.</summary>
    public static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("?", "\\?", StringComparison.Ordinal);
    }
}

/// <summary>
/// Alinea búsquedas con nombres en español: la consulta sin tildes debe coincidir con "Láctea", etc.
/// </summary>
internal static class StoreSearchTextNormalize
{
    public static string CollapseWhitespace(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string RemoveDiacritics(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Espacios normalizados + sin diacríticos (para texto analizado / frases).</summary>
    public static string FoldForMatch(string? s) => RemoveDiacritics(CollapseWhitespace(s));

    /// <summary>Igual que <see cref="FoldForMatch"/> en minúsculas (keyword <c>vtCatalogSk</c>, wildcard, memoria).</summary>
    public static string FoldLowerKeyword(string? s) => FoldForMatch(s).ToLowerInvariant();
}

internal static class StoreSearchCategoryParser
{
    public static IReadOnlyList<string> ParseCategories(IReadOnlyList<string>? categories) =>
        CatalogJsonColumnParsing
            .StringListOrEmpty(categories)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
}

/// <summary>Helpers de texto para <see cref="VibeTrade.Backend.Features.Search.Elasticsearch.CatalogSearchDocumentFactory"/> (embedding / campo SearchText).</summary>
internal static class CatalogSearchEmbeddingTextUtils
{
    internal const int MaxFieldChars = 6000;

    public static string ServiceRiesgosToSearchText(ServiceRiesgosBody? r) =>
        r is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', r.Items) : "";

    public static string ServiceItemsBodyToSearchText(ServiceDependenciasBody? b) =>
        b is { Enabled: true, Items: { Count: > 0 } } ? string.Join(' ', b.Items) : "";

    public static string ServiceGarantiasToSearchText(ServiceGarantiasBody? g) =>
        g is { Enabled: true, Texto: { Length: > 0 } } ? (g.Texto ?? "").Trim() : "";

    public static string CustomFieldsToSearchText(IReadOnlyList<StoreCustomFieldBody>? fields)
    {
        if (fields is not { Count: > 0 })
            return "";
        return string.Join(' ', fields.Select(FlattenCustomField));
    }

    private static string FlattenCustomField(StoreCustomFieldBody f)
    {
        var parts = new List<string>();
        foreach (var s in new[] { f.Title, f.Body, f.AttachmentNote })
        {
            if (!string.IsNullOrWhiteSpace(s))
                parts.Add(s.Trim());
        }

        if (f.Attachments is { Count: > 0 } a)
        {
            foreach (var x in a)
            {
                var t = (x.FileName + " " + x.Url).Trim();
                if (t.Length > 0)
                    parts.Add(t);
            }
        }

        return string.Join(' ', parts);
    }

    public static string OfferQaToSearchText(IReadOnlyList<OfferQaComment>? items)
    {
        if (items is not { Count: > 0 })
            return "";
        return string.Join(
            ' ',
            items.Select(c =>
            {
                var parts = new List<string> { c.Text, c.Question, c.Answer }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!);
                return string.Join(' ', parts);
            }));
    }

    public static void AppendFoldedLine(StringBuilder sb, params string?[] parts)
    {
        var folded = string.Join(' ', parts.Select(p => StoreSearchTextNormalize.FoldForMatch(p)).Where(x => x.Length > 0));
        if (folded.Length == 0)
            return;
        AppendLine(sb, "Folded", folded);
    }

    public static void AppendLine(StringBuilder sb, string label, string? value)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
            return;
        var t = Trunc(trimmed);
        if (t.Length == 0)
            return;
        sb.Append(label);
        sb.Append(": ");
        sb.AppendLine(t);
    }

    public static string? Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Trim();
    }

    public static string Trunc(string s)
    {
        if (s.Length <= MaxFieldChars)
            return s;
        return s[..MaxFieldChars] + "…";
    }

    public static string CategoriesToPlain(IReadOnlyList<string>? categories)
    {
        var parts = CatalogJsonColumnParsing.StringListOrEmpty(categories)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        return parts.Count == 0 ? "" : string.Join(' ', parts);
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        return string.Join('\n', s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
