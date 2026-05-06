using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

internal static class MarketCatalogRowViewFactory
{
    public static StoreProductCatalogRowView ProductFromRow(StoreProductRow p)
    {
        var o = new StoreProductCatalogRowView
        {
            Id = p.Id,
            StoreId = p.StoreId,
            Category = p.Category,
            Name = p.Name,
            ShortDescription = p.ShortDescription,
            MainBenefit = p.MainBenefit,
            TechnicalSpecs = p.TechnicalSpecs,
            Model = string.IsNullOrEmpty(p.Model) ? null : p.Model,
            Condition = p.Condition,
            Price = p.Price,
            MonedaPrecio = string.IsNullOrEmpty(p.MonedaPrecio) ? null : p.MonedaPrecio,
            Monedas = CatalogJsonColumnParsing.StringListOrEmpty(p.Monedas),
            Availability = p.Availability,
            WarrantyReturn = p.WarrantyReturn,
            ContentIncluded = p.ContentIncluded,
            UsageConditions = p.UsageConditions,
            Published = p.Published,
            TaxesShippingInstall = string.IsNullOrEmpty(p.TaxesShippingInstall) ? null : p.TaxesShippingInstall,
            TransportIncluded = p.TransportIncluded,
            PhotoUrls = CatalogJsonColumnParsing.StringListOrEmpty(p.PhotoUrls),
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(p.CustomFields),
            Qa = p.OfferQa ?? new List<OfferQaComment>(),
        };
        return o;
    }

    public static StoreServiceCatalogRowView ServiceFromRow(StoreServiceRow s)
    {
        var urls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(s.PhotoUrls);
        return new StoreServiceCatalogRowView
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Category = s.Category,
            TipoServicio = s.TipoServicio,
            Descripcion = s.Descripcion,
            Incluye = s.Incluye,
            NoIncluye = s.NoIncluye,
            Entregables = s.Entregables,
            PropIntelectual = s.PropIntelectual,
            Published = s.Published,
            Monedas = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas),
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(s.CustomFields),
            PhotoUrls = urls,
            Riesgos = s.Riesgos,
            Dependencias = s.Dependencias,
            Garantias = s.Garantias,
            Qa = s.OfferQa ?? new List<OfferQaComment>(),
        };
    }
}
