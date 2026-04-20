using System.Text.Json;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogJsonHelpers
{
    public static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static string SerializeStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return "[]";
        return p.GetRawText();
    }

    public static string? SerializeJsonElement(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var p) ? p.GetRawText() : null;

    public static bool TryGetStoresObject(JsonElement workspaceRoot, out JsonElement storesEl)
    {
        storesEl = default;
        if (!workspaceRoot.TryGetProperty("stores", out var s) || s.ValueKind != JsonValueKind.Object)
            return false;
        storesEl = s;
        return true;
    }

    public static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
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

}
