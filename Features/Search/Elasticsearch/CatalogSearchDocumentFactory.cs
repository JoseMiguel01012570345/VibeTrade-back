using System.Text;
using Elastic.Clients.Elasticsearch;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Search.Elasticsearch;

/// <summary>
/// Construye <see cref="VibeTrade.Backend.Features.Search.Dtos.CatalogSearchDocument"/> y el texto semántico para embedding TF‑IDF / campo <c>SearchText</c>.
/// </summary>
internal static class CatalogSearchDocumentFactory
{
    public static CatalogSearchDocument FromStore(StoreRow store, long publishedProducts, long publishedServices)
    {
        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var name = (store.Name ?? "").Trim();
        var cats = StoreSearchCategoryParser.ParseCategories(store.Categories);
        return new CatalogSearchDocument
        {
            Kind = CatalogSearchKinds.Store,
            StoreId = store.Id,
            OfferId = "",
            Name = name,
            VtCatalogSk = StoreSearchTextNormalize.FoldLowerKeyword(name),
            Categories = cats,
            SearchText = EmbeddingTextForStore(store),
            Location = location,
            VtGeoPoint = location,
            TrustScore = store.TrustScore,
            PublishedProducts = publishedProducts,
            PublishedServices = publishedServices,
        };
    }

    public static CatalogSearchDocument? FromProduct(StoreProductRow p, StoreRow store, long pubProducts, long pubServices)
    {
        if (!p.Published)
            return null;

        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var name = (p.Name ?? "").Trim();
        var cat = (p.Category ?? "").Trim();
        var cats = string.IsNullOrEmpty(cat) ? Array.Empty<string>() : new[] { cat };
        var displayName = string.IsNullOrEmpty(name) ? cat : name;

        return new CatalogSearchDocument
        {
            Kind = CatalogSearchKinds.Product,
            StoreId = store.Id,
            OfferId = p.Id,
            Name = displayName,
            VtCatalogSk = StoreSearchTextNormalize.FoldLowerKeyword(displayName),
            Categories = cats,
            SearchText = EmbeddingTextForProduct(p, store),
            Location = location,
            VtGeoPoint = location,
            TrustScore = store.TrustScore,
            PublishedProducts = pubProducts,
            PublishedServices = pubServices,
        };
    }

    public static CatalogSearchDocument? FromService(StoreServiceRow s, StoreRow store, long pubProducts, long pubServices)
    {
        if (s.Published == false)
            return null;

        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var title = (s.TipoServicio ?? "").Trim();
        if (title.Length == 0)
            title = (s.Category ?? "").Trim();
        var cat = (s.Category ?? "").Trim();
        var cats = string.IsNullOrEmpty(cat) ? Array.Empty<string>() : new[] { cat };

        return new CatalogSearchDocument
        {
            Kind = CatalogSearchKinds.Service,
            StoreId = store.Id,
            OfferId = s.Id,
            Name = title,
            VtCatalogSk = StoreSearchTextNormalize.FoldLowerKeyword(title),
            Categories = cats,
            SearchText = EmbeddingTextForService(s, store),
            Location = location,
            VtGeoPoint = location,
            TrustScore = store.TrustScore,
            PublishedProducts = pubProducts,
            PublishedServices = pubServices,
        };
    }

    public static CatalogSearchDocument? FromEmergent(
        EmergentOfferRow e,
        StoreRow store,
        StoreProductRow? p,
        StoreServiceRow? s,
        long pubProducts,
        long pubServices)
    {
        if (e.RetractedAtUtc is not null)
            return null;

        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        var title = (snap.Titulo ?? "").Trim();
        if (title.Length == 0 && p is not null)
            title = (p.Name ?? "").Trim();
        if (title.Length == 0 && s is not null)
            title = (s.TipoServicio ?? "").Trim();
        if (title.Length == 0)
            title = "Hoja de ruta";

        var cat = p?.Category ?? s?.Category ?? "";
        cat = cat.Trim();
        var cats = string.IsNullOrEmpty(cat) ? Array.Empty<string>() : new[] { cat };

        return new CatalogSearchDocument
        {
            Kind = CatalogSearchKinds.Emergent,
            StoreId = store.Id,
            OfferId = e.Id,
            Name = title,
            VtCatalogSk = StoreSearchTextNormalize.FoldLowerKeyword(title),
            Categories = cats,
            SearchText = EmbeddingTextForEmergent(e, store, p, s),
            Location = location,
            VtGeoPoint = location,
            TrustScore = store.TrustScore,
            PublishedProducts = pubProducts,
            PublishedServices = pubServices,
        };
    }

    private static string EmbeddingTextForStore(StoreRow s)
    {
        var sb = new StringBuilder();
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Name", s.Name);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "NormalizedName", s.NormalizedName);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Pitch", s.Pitch);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Website", s.WebsiteUrl);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Categories", CatalogSearchEmbeddingTextUtils.CategoriesToPlain(s.Categories));
        CatalogSearchEmbeddingTextUtils.AppendFoldedLine(sb, s.Name, s.NormalizedName, s.Pitch, CatalogSearchEmbeddingTextUtils.CategoriesToPlain(s.Categories));
        if (s.TransportIncluded)
            sb.AppendLine("TransportIncluded: envío incluido");
        return CatalogSearchEmbeddingTextUtils.Normalize(sb.ToString());
    }

    private static string EmbeddingTextForProduct(StoreProductRow p, StoreRow store)
    {
        var sb = new StringBuilder();
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StoreName", store.Name);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StorePitch", store.Pitch);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Categories", CatalogSearchEmbeddingTextUtils.CategoriesToPlain(store.Categories));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Name", p.Name);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Category", p.Category);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Model", p.Model);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "ShortDescription", p.ShortDescription);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "MainBenefit", p.MainBenefit);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "TechnicalSpecs", p.TechnicalSpecs);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Condition", p.Condition);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Price", p.Price);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "TaxesShippingInstall", p.TaxesShippingInstall);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Availability", p.Availability);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "WarrantyReturn", p.WarrantyReturn);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "ContentIncluded", p.ContentIncluded);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "UsageConditions", p.UsageConditions);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "CustomFields", CatalogSearchEmbeddingTextUtils.CustomFieldsToSearchText(p.CustomFields));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "OfferQa", CatalogSearchEmbeddingTextUtils.OfferQaToSearchText(p.OfferQa));
        CatalogSearchEmbeddingTextUtils.AppendFoldedLine(sb, store.Name, p.Name, p.Category, p.ShortDescription);
        return CatalogSearchEmbeddingTextUtils.Normalize(sb.ToString());
    }

    private static string EmbeddingTextForService(StoreServiceRow sv, StoreRow store)
    {
        var sb = new StringBuilder();
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StoreName", store.Name);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StorePitch", store.Pitch);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Categories", CatalogSearchEmbeddingTextUtils.CategoriesToPlain(store.Categories));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Category", sv.Category);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "TipoServicio", sv.TipoServicio);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Descripcion", sv.Descripcion);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Incluye", sv.Incluye);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "NoIncluye", sv.NoIncluye);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Entregables", sv.Entregables);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "PropIntelectual", sv.PropIntelectual);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Riesgos", CatalogSearchEmbeddingTextUtils.ServiceRiesgosToSearchText(sv.Riesgos));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Dependencias", CatalogSearchEmbeddingTextUtils.ServiceItemsBodyToSearchText(sv.Dependencias));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Garantias", CatalogSearchEmbeddingTextUtils.ServiceGarantiasToSearchText(sv.Garantias));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "CustomFields", CatalogSearchEmbeddingTextUtils.CustomFieldsToSearchText(sv.CustomFields));
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "OfferQa", CatalogSearchEmbeddingTextUtils.OfferQaToSearchText(sv.OfferQa));
        CatalogSearchEmbeddingTextUtils.AppendFoldedLine(sb, store.Name, sv.TipoServicio, sv.Category, sv.Descripcion);
        return CatalogSearchEmbeddingTextUtils.Normalize(sb.ToString());
    }

    private static string EmbeddingTextForEmergent(EmergentOfferRow e, StoreRow store, StoreProductRow? p, StoreServiceRow? s)
    {
        var sb = new StringBuilder();
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StoreName", store.Name);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "StorePitch", store.Pitch);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Categories", CatalogSearchEmbeddingTextUtils.CategoriesToPlain(store.Categories));
        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "EmergentTitulo", snap.Titulo);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "MercanciasResumen", snap.MercanciasResumen);
        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "MonedaPago", snap.MonedaPago);
        foreach (var leg in snap.Paradas ?? [])
        {
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "Parada", $"{leg.Origen} → {leg.Destino}");
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "ParadaPrecio", leg.PrecioTransportista);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "ParadaDetalle", $"{leg.Origen} {leg.Destino} {leg.PrecioTransportista} {leg.MonedaPago}");
        }

        if (p is not null)
        {
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseProductName", p.Name);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseProductCategory", p.Category);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseProductModel", p.Model);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseProductShortDescription", p.ShortDescription);
        }

        if (s is not null)
        {
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseServiceCategory", s.Category);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseServiceTipo", s.TipoServicio);
            CatalogSearchEmbeddingTextUtils.AppendLine(sb, "BaseServiceDescripcion", s.Descripcion);
        }

        CatalogSearchEmbeddingTextUtils.AppendLine(sb, "OfferQa", CatalogSearchEmbeddingTextUtils.OfferQaToSearchText(e.OfferQa));
        CatalogSearchEmbeddingTextUtils.AppendFoldedLine(
            sb,
            store.Name,
            snap.Titulo,
            snap.MercanciasResumen,
            p?.Name,
            p?.Category,
            s?.TipoServicio,
            s?.Category);
        sb.AppendLine("EmergentRoutePublication: hoja de ruta publicada");
        return CatalogSearchEmbeddingTextUtils.Normalize(sb.ToString());
    }
}
