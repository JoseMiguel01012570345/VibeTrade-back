using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market.Offers;

internal static class HomeOfferViewFactory
{
    public static HomeOfferViewDto FromProductRow(StoreProductRow p)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Category))
            tags.Add(p.Category.Trim());
        if (!string.IsNullOrWhiteSpace(p.Condition))
            tags.Add(p.Condition.Trim());
        tags.Add("Producto");

        var price = FormatProductPrice(p);
        var title = string.IsNullOrWhiteSpace(p.Name) ? "Producto" : p.Name.Trim();
        var photoUrls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(p.PhotoUrls);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : null;
        var accepted = CatalogJsonColumnParsing.StringListOrEmpty(p.Monedas);
        var currency = (p.MonedaPrecio ?? "").Trim();

        return new HomeOfferViewDto
        {
            Id = p.Id,
            StoreId = p.StoreId,
            Title = title,
            Price = price,
            Currency = currency.Length == 0 ? null : currency,
            AcceptedCurrencies = accepted,
            Description = OfferDescriptionForProduct(p),
            Tags = tags,
            ImageUrl = primary,
            ImageUrls = photoUrls,
            Qa = p.OfferQa ?? new List<OfferQaComment>(),
        };
    }

    public static HomeOfferViewDto FromServiceRow(StoreServiceRow s)
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
        var imageUrls = photoUrls.Count > 0
            ? (IReadOnlyList<string>)photoUrls
            : new[] { MarketCatalogConstants.DefaultServiceOfferImageUrl };

        var accepted = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas);
        var o = new HomeOfferViewDto
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Title = title,
            Price = FormatServicePriceLine(s),
            AcceptedCurrencies = accepted,
            Description = (s.Descripcion ?? "").Trim(),
            Tags = tags,
            ImageUrl = primary,
            ImageUrls = imageUrls,
            Qa = s.OfferQa ?? new List<OfferQaComment>(),
        };
        if (!string.IsNullOrWhiteSpace(s.Category))
            o.Category = s.Category.Trim();
        if (!string.IsNullOrWhiteSpace(s.TipoServicio))
            o.TipoServicio = s.TipoServicio.Trim();
        if (!string.IsNullOrWhiteSpace(s.Incluye))
            o.Incluye = s.Incluye.Trim();
        if (!string.IsNullOrWhiteSpace(s.NoIncluye))
            o.NoIncluye = s.NoIncluye.Trim();
        if (!string.IsNullOrWhiteSpace(s.Entregables))
            o.Entregables = s.Entregables.Trim();
        if (!string.IsNullOrWhiteSpace(s.PropIntelectual))
            o.PropIntelectual = s.PropIntelectual.Trim();
        return o;
    }

    private static string OfferDescriptionForProduct(StoreProductRow p)
    {
        var a = (p.ShortDescription ?? "").Trim();
        if (a.Length > 0)
            return a;
        var b = (p.MainBenefit ?? "").Trim();
        return b.Length > 0 ? b : "";
    }

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
            var codes = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas)
                .Select(x => x.Trim())
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
