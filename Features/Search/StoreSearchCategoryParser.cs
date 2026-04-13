using System.Text.Json;

namespace VibeTrade.Backend.Features.Search;

internal static class StoreSearchCategoryParser
{
    public static IReadOnlyList<string> ParseCategories(string? categoriesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(categoriesJson) ? "[]" : categoriesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        list.Add(s);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
