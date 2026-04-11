using System.Text.Json;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogProductRowMapper
{
    public static void Apply(JsonElement item, StoreProductRow row, DateTimeOffset now)
    {
        row.Category = MarketCatalogJsonHelpers.GetString(item, "category") ?? "";
        row.Name = MarketCatalogJsonHelpers.GetString(item, "name") ?? "";
        row.Model = MarketCatalogJsonHelpers.GetString(item, "model");
        row.ShortDescription = MarketCatalogJsonHelpers.GetString(item, "shortDescription") ?? "";
        row.MainBenefit = MarketCatalogJsonHelpers.GetString(item, "mainBenefit") ?? "";
        row.TechnicalSpecs = MarketCatalogJsonHelpers.GetString(item, "technicalSpecs") ?? "";
        row.Condition = MarketCatalogJsonHelpers.GetString(item, "condition") ?? "";
        row.Price = MarketCatalogJsonHelpers.GetString(item, "price") ?? "";
        row.MonedaPrecio = MarketCatalogJsonHelpers.GetString(item, "monedaPrecio");
        row.MonedasJson = MarketCatalogCurrency.SerializeMonedasFromCatalogItemJson(item);
        row.TaxesShippingInstall = MarketCatalogJsonHelpers.GetString(item, "taxesShippingInstall");
        row.Availability = MarketCatalogJsonHelpers.GetString(item, "availability") ?? "";
        row.WarrantyReturn = MarketCatalogJsonHelpers.GetString(item, "warrantyReturn") ?? "";
        row.ContentIncluded = MarketCatalogJsonHelpers.GetString(item, "contentIncluded") ?? "";
        row.UsageConditions = MarketCatalogJsonHelpers.GetString(item, "usageConditions") ?? "";
        row.Published = item.TryGetProperty("published", out var pub) && pub.ValueKind == JsonValueKind.True;
        row.PhotoUrlsJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "photoUrls") ?? "[]";
        row.CustomFieldsJson = MarketCatalogJsonHelpers.SerializeJsonElement(item, "customFields") ?? "[]";
        row.UpdatedAt = now;
    }
}
