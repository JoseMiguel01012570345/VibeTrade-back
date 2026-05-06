using System.Globalization;
using System.Text;

namespace VibeTrade.Backend.Features.Search.Catalog;

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
