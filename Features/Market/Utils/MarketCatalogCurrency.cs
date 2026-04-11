using System.Text.Json;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogCurrency
{
    public static void ThrowIfProductCurrencyInvalid(JsonElement item, string id)
    {
        if (string.IsNullOrWhiteSpace(MarketCatalogJsonHelpers.GetString(item, "monedaPrecio")))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": la moneda del precio es obligatoria.");
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(item))
            throw new CatalogCurrencyValidationException(
                $"Producto \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    public static void ThrowIfServiceCurrencyInvalid(JsonElement item, string id)
    {
        if (!CatalogItemHasAtLeastOneAcceptedMoneda(item))
            throw new CatalogCurrencyValidationException(
                $"Servicio \"{id}\": indicá al menos una moneda aceptada para el pago.");
    }

    public static bool CatalogItemHasAtLeastOneAcceptedMoneda(JsonElement item)
    {
        if (item.TryGetProperty("monedas", out var m) && m.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in m.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                    return true;
            }
        }

        return !string.IsNullOrWhiteSpace(MarketCatalogJsonHelpers.GetString(item, "moneda"));
    }

    public static string SerializeMonedasFromCatalogItemJson(JsonElement item)
    {
        if (item.TryGetProperty("monedas", out var m) && m.ValueKind == JsonValueKind.Array)
            return m.GetRawText();
        var legacy = MarketCatalogJsonHelpers.GetString(item, "moneda");
        if (!string.IsNullOrEmpty(legacy))
            return JsonSerializer.Serialize(new[] { legacy });
        return "[]";
    }
}
