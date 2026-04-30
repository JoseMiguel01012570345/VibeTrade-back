using System.Text.Json.Serialization;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Producto en <c>storeCatalogs[id].products[]</c> (lectura, mismo contrato que persistencia/PUT).</summary>
public sealed class StoreProductCatalogRowView
{
    public string Id { get; set; } = "";
    public string StoreId { get; set; } = "";
    public string? Category { get; set; }
    public string? Name { get; set; }
    public string? ShortDescription { get; set; }
    public string? MainBenefit { get; set; }
    public string? TechnicalSpecs { get; set; }
    public string? Model { get; set; }
    public string? Condition { get; set; }
    public string? Price { get; set; }
    [JsonPropertyName("monedaPrecio")]
    public string? MonedaPrecio { get; set; }
    public IReadOnlyList<string> Monedas { get; set; } = Array.Empty<string>();
    public string? Availability { get; set; }
    public string? WarrantyReturn { get; set; }
    public string? ContentIncluded { get; set; }
    public string? UsageConditions { get; set; }
    public string? TaxesShippingInstall { get; set; }
    public bool TransportIncluded { get; set; }
    public bool Published { get; set; }
    public IReadOnlyList<string> PhotoUrls { get; set; } = Array.Empty<string>();
    public IReadOnlyList<StoreCustomFieldBody> CustomFields { get; set; } = Array.Empty<StoreCustomFieldBody>();
    public IReadOnlyList<OfferQaComment> Qa { get; set; } = Array.Empty<OfferQaComment>();
    [JsonPropertyName("publicCommentCount")]
    public int? PublicCommentCount { get; set; }
    [JsonPropertyName("offerLikeCount")]
    public int? OfferLikeCount { get; set; }
    [JsonPropertyName("viewerLikedOffer")]
    public bool? ViewerLikedOffer { get; set; }

    public StoreProductPutRequest ToPutRequest() =>
        new()
        {
            Id = Id,
            StoreId = StoreId,
            Category = Category,
            Name = Name,
            Model = Model,
            ShortDescription = ShortDescription,
            MainBenefit = MainBenefit,
            TechnicalSpecs = TechnicalSpecs,
            Condition = Condition,
            Price = Price,
            MonedaPrecio = MonedaPrecio,
            Monedas = Monedas.ToList(),
            TaxesShippingInstall = TaxesShippingInstall,
            TransportIncluded = TransportIncluded,
            Availability = Availability,
            WarrantyReturn = WarrantyReturn,
            ContentIncluded = ContentIncluded,
            UsageConditions = UsageConditions,
            Published = Published,
            PhotoUrls = PhotoUrls.ToList(),
            CustomFields = CustomFields is List<StoreCustomFieldBody> l ? l : CustomFields.ToList(),
        };
}

/// <summary>Servicio en <c>storeCatalogs[id].services[]</c>.</summary>
public sealed class StoreServiceCatalogRowView
{
    public string Id { get; set; } = "";
    public string StoreId { get; set; } = "";
    public string? Category { get; set; }
    public string? TipoServicio { get; set; }
    public string? Descripcion { get; set; }
    public string? Incluye { get; set; }
    public string? NoIncluye { get; set; }
    public string? Entregables { get; set; }
    public string? PropIntelectual { get; set; }
    public bool? Published { get; set; }
    public IReadOnlyList<string> Monedas { get; set; } = Array.Empty<string>();
    public IReadOnlyList<StoreCustomFieldBody> CustomFields { get; set; } = Array.Empty<StoreCustomFieldBody>();
    public IReadOnlyList<string> PhotoUrls { get; set; } = Array.Empty<string>();
    public ServiceRiesgosBody? Riesgos { get; set; }
    public ServiceDependenciasBody? Dependencias { get; set; }
    public ServiceGarantiasBody? Garantias { get; set; }
    public IReadOnlyList<OfferQaComment> Qa { get; set; } = Array.Empty<OfferQaComment>();
    [JsonPropertyName("publicCommentCount")]
    public int? PublicCommentCount { get; set; }
    [JsonPropertyName("offerLikeCount")]
    public int? OfferLikeCount { get; set; }
    [JsonPropertyName("viewerLikedOffer")]
    public bool? ViewerLikedOffer { get; set; }

    public StoreServicePutRequest ToPutRequest() =>
        new()
        {
            Id = Id,
            StoreId = StoreId,
            Category = Category,
            TipoServicio = TipoServicio,
            Monedas = Monedas.ToList(),
            Descripcion = Descripcion,
            Incluye = Incluye,
            NoIncluye = NoIncluye,
            Entregables = Entregables,
            PropIntelectual = PropIntelectual,
            Riesgos = Riesgos,
            Dependencias = Dependencias,
            Garantias = Garantias,
            PhotoUrls = PhotoUrls.ToList(),
            CustomFields = CustomFields is List<StoreCustomFieldBody> l ? l : CustomFields.ToList(),
            Published = Published,
        };
}

/// <summary>Bloque <c>storeCatalogs[storeId]</c> en el workspace o detalle de tienda.</summary>
public sealed class StoreCatalogBlockView
{
    public string? Pitch { get; set; }
    public long? JoinedAt { get; set; }
    public IReadOnlyList<StoreProductCatalogRowView> Products { get; set; } = Array.Empty<StoreProductCatalogRowView>();
    public IReadOnlyList<StoreServiceCatalogRowView> Services { get; set; } = Array.Empty<StoreServiceCatalogRowView>();
}

/// <summary><c>{"store":...,"catalog":...}</c> en detalle de tienda.</summary>
public sealed class StoreWithCatalogDetailView
{
    [JsonPropertyName("store")]
    public StoreProfileWorkspaceData Store { get; set; } = null!;

    [JsonPropertyName("catalog")]
    public StoreCatalogBlockView Catalog { get; set; } = null!;

    [JsonPropertyName("viewer")]
    public StoreDetailViewerView? Viewer { get; set; }

    [JsonPropertyName("owner")]
    public StoreDetailOwnerView? Owner { get; set; }
}

public sealed class StoreDetailViewerView
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }
    public string? Role { get; set; }
}

public sealed class StoreDetailOwnerView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
    [JsonPropertyName("trustScore")]
    public int TrustScore { get; set; }
}

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
