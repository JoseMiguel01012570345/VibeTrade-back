using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogOfferJsonBuilder
{
    public static JsonObject ProductRowToOfferJson(StoreProductRow p)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Category))
            tags.Add(p.Category.Trim());
        if (!string.IsNullOrWhiteSpace(p.Condition))
            tags.Add(p.Condition.Trim());
        tags.Add("Producto");

        var price = FormatProductPrice(p);
        var title = string.IsNullOrWhiteSpace(p.Name) ? "Producto" : p.Name.Trim();
        var photoUrls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(p.PhotoUrlsJson);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : null;

        var accepted = TryParseArray(p.MonedasJson);
        var currency = (p.MonedaPrecio ?? "").Trim();

        return new JsonObject
        {
            ["id"] = p.Id,
            ["storeId"] = p.StoreId,
            ["title"] = title,
            ["price"] = price,
            ["currency"] = currency.Length == 0 ? null : currency,
            ["acceptedCurrencies"] = accepted,
            ["description"] = OfferDescriptionForProduct(p),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["imageUrl"] = primary,
            ["imageUrls"] = new JsonArray(photoUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray()),
            ["qa"] = OfferQaJson.ToJsonNode(p.OfferQa),
        };
    }

    public static JsonObject ServiceRowToOfferJson(StoreServiceRow s)
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
        var photoUrls = MarketCatalogPhotoRules.CollectServiceOfferGalleryUrls(s);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : MarketCatalogConstants.DefaultServiceOfferImageUrl;
        var imageUrlsNode = photoUrls.Count > 0
            ? new JsonArray(photoUrls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray())
            : new JsonArray((JsonNode?)JsonValue.Create(MarketCatalogConstants.DefaultServiceOfferImageUrl));

        var accepted = TryParseArray(s.MonedasJson);

        return new JsonObject
        {
            ["id"] = s.Id,
            ["storeId"] = s.StoreId,
            ["title"] = title,
            ["price"] = FormatServicePriceLine(s),
            ["acceptedCurrencies"] = accepted,
            ["description"] = OfferDescriptionForService(s),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["imageUrl"] = primary,
            ["imageUrls"] = imageUrlsNode,
            ["qa"] = OfferQaJson.ToJsonNode(s.OfferQa),
        };
    }

    private static JsonNode TryParseArray(string? json)
    {
        try
        {
            return JsonNode.Parse(json ?? "[]") ?? new JsonArray();
        }
        catch
        {
            return new JsonArray();
        }
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
}
