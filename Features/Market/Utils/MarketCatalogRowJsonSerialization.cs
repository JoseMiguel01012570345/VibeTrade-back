using System.Text.Json.Nodes;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogRowJsonSerialization
{
    public static JsonObject ProductToJson(StoreProductRow p)
    {
        var o = new JsonObject
        {
            ["id"] = p.Id,
            ["storeId"] = p.StoreId,
            ["category"] = p.Category,
            ["name"] = p.Name,
            ["shortDescription"] = p.ShortDescription,
            ["mainBenefit"] = p.MainBenefit,
            ["technicalSpecs"] = p.TechnicalSpecs,
            ["condition"] = p.Condition,
            ["price"] = p.Price,
            ["availability"] = p.Availability,
            ["warrantyReturn"] = p.WarrantyReturn,
            ["contentIncluded"] = p.ContentIncluded,
            ["usageConditions"] = p.UsageConditions,
            ["published"] = p.Published,
        };
        if (!string.IsNullOrEmpty(p.Model))
            o["model"] = p.Model;
        if (!string.IsNullOrEmpty(p.MonedaPrecio))
            o["monedaPrecio"] = p.MonedaPrecio;
        o["monedas"] = TryParseArray(p.MonedasJson);
        if (!string.IsNullOrEmpty(p.TaxesShippingInstall))
            o["taxesShippingInstall"] = p.TaxesShippingInstall;
        o["photoUrls"] = TryParseArray(p.PhotoUrlsJson);
        o["customFields"] = TryParseArray(p.CustomFieldsJson);
        o["qa"] = TryParseArray(p.OfferQaJson);
        return o;
    }

    public static JsonObject ServiceToJson(StoreServiceRow s)
    {
        var o = new JsonObject
        {
            ["id"] = s.Id,
            ["storeId"] = s.StoreId,
            ["category"] = s.Category,
            ["tipoServicio"] = s.TipoServicio,
            ["descripcion"] = s.Descripcion,
            ["incluye"] = s.Incluye,
            ["noIncluye"] = s.NoIncluye,
            ["entregables"] = s.Entregables,
            ["propIntelectual"] = s.PropIntelectual,
        };
        if (s.Published.HasValue)
            o["published"] = s.Published.Value;
        o["riesgos"] = TryParseObject(s.RiesgosJson);
        o["dependencias"] = TryParseObject(s.DependenciasJson);
        o["garantias"] = TryParseObject(s.GarantiasJson);
        o["monedas"] = TryParseArray(s.MonedasJson);
        o["customFields"] = TryParseArray(s.CustomFieldsJson);
        var urls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(s.PhotoUrlsJson);
        o["photoUrls"] = urls.Count > 0
            ? new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray())
            : new JsonArray();
        o["qa"] = TryParseArray(s.OfferQaJson);
        return o;
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

    private static JsonNode TryParseObject(string? json)
    {
        try
        {
            return JsonNode.Parse(json ?? "{}") ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
