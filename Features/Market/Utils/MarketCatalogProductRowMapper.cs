using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogProductRowMapper
{
    public static void Apply(StoreProductPutRequest p, StoreProductRow row, DateTimeOffset now)
    {
        row.Category = p.Category ?? "";
        row.Name = p.Name ?? "";
        row.Model = p.Model;
        row.ShortDescription = p.ShortDescription ?? "";
        row.MainBenefit = p.MainBenefit ?? "";
        row.TechnicalSpecs = p.TechnicalSpecs ?? "";
        row.Condition = p.Condition ?? "";
        row.Price = p.Price ?? "";
        row.MonedaPrecio = p.MonedaPrecio;
        row.Monedas = MarketCatalogCurrency.BuildMonedasList(p);
        row.TaxesShippingInstall = p.TaxesShippingInstall;
        row.Availability = p.Availability ?? "";
        row.WarrantyReturn = p.WarrantyReturn ?? "";
        row.ContentIncluded = p.ContentIncluded ?? "";
        row.UsageConditions = p.UsageConditions ?? "";
        row.Published = p.Published == true;
        row.PhotoUrls = p.PhotoUrls is { Count: > 0 } ? p.PhotoUrls.ToList() : new List<string>();
        row.CustomFields = p.CustomFields is not null
            ? p.CustomFields.ToList()
            : row.CustomFields;
        row.UpdatedAt = now;
    }
}
