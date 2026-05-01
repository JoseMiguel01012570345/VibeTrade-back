using System.Text.Json.Serialization;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Shape alineado a <c>Offer</c> en Home / feed / búsqueda (sin JSON opaco).</summary>
public sealed class HomeOfferViewDto
{
    public string Id { get; set; } = "";
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = "";
    public string? Title { get; set; }
    public string? Price { get; set; }
    public string? Currency { get; set; }
    [JsonPropertyName("acceptedCurrencies")]
    public IReadOnlyList<string> AcceptedCurrencies { get; set; } = Array.Empty<string>();
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
    [JsonPropertyName("imageUrls")]
    public IReadOnlyList<string> ImageUrls { get; set; } = Array.Empty<string>();
    /// <summary>Null si el JSON no incluye <c>qa</c> (p. ej. parche solo con otras propiedades).</summary>
    public List<OfferQaComment>? Qa { get; set; }

    public string? Category { get; set; }
    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }
    public string? Incluye { get; set; }
    [JsonPropertyName("noIncluye")]
    public string? NoIncluye { get; set; }
    public string? Entregables { get; set; }
    [JsonPropertyName("propIntelectual")]
    public string? PropIntelectual { get; set; }

    [JsonPropertyName("emergentBaseOfferId")]
    public string? EmergentBaseOfferId { get; set; }
    [JsonPropertyName("emergentThreadId")]
    public string? EmergentThreadId { get; set; }
    [JsonPropertyName("emergentRouteSheetId")]
    public string? EmergentRouteSheetId { get; set; }
    [JsonPropertyName("isEmergentRoutePublication")]
    public bool? IsEmergentRoutePublication { get; set; }
    [JsonPropertyName("emergentMonedaPago")]
    public string? EmergentMonedaPago { get; set; }
    [JsonPropertyName("emergentRouteParadas")]
    public IReadOnlyList<EmergentRouteParadaViewDto> EmergentRouteParadas { get; set; } = Array.Empty<EmergentRouteParadaViewDto>();

    [JsonPropertyName("publicCommentCount")]
    public int? PublicCommentCount { get; set; }
    [JsonPropertyName("offerLikeCount")]
    public int? OfferLikeCount { get; set; }
    [JsonPropertyName("viewerLikedOffer")]
    public bool? ViewerLikedOffer { get; set; }
}

public sealed class EmergentRouteParadaViewDto
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public int Orden { get; set; }
    [JsonPropertyName("stopId")]
    public string? StopId { get; set; }
    [JsonPropertyName("origenLat")]
    public string? OrigenLat { get; set; }
    [JsonPropertyName("origenLng")]
    public string? OrigenLng { get; set; }
    [JsonPropertyName("destinoLat")]
    public string? DestinoLat { get; set; }
    [JsonPropertyName("destinoLng")]
    public string? DestinoLng { get; set; }
    [JsonPropertyName("monedaPago")]
    public string? MonedaPago { get; set; }
    [JsonPropertyName("precioTransportista")]
    public string? PrecioTransportista { get; set; }

    [JsonPropertyName("osrmRoadKm")]
    public double? OsrmRoadKm { get; set; }

    [JsonPropertyName("osrmRouteLatLngs")]
    public List<List<double>>? OsrmRouteLatLngs { get; set; }
}

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
