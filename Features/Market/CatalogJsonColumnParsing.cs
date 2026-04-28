using System.Text.Json;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Parse de JSON en el límite hacia estructuras (p. ej. <c>demo-seed</c> o columnas leídas como texto en migraciones heredadas).</summary>
internal static class CatalogJsonColumnParsing
{
    public static IReadOnlyList<string> StringListOrEmpty(IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
            return Array.Empty<string>();
        return values
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static IReadOnlyList<string> StringListOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, MarketJsonDefaults.Options) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<StoreCustomFieldBody> CustomFieldsListOrEmpty(IReadOnlyList<StoreCustomFieldBody>? values)
    {
        if (values is not { Count: > 0 })
            return Array.Empty<StoreCustomFieldBody>();
        return values;
    }

    public static IReadOnlyList<StoreCustomFieldBody> CustomFieldsListOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<StoreCustomFieldBody>();
        try
        {
            return JsonSerializer.Deserialize<List<StoreCustomFieldBody>>(json, MarketJsonDefaults.Options)
                ?? new List<StoreCustomFieldBody>();
        }
        catch
        {
            return Array.Empty<StoreCustomFieldBody>();
        }
    }
}
