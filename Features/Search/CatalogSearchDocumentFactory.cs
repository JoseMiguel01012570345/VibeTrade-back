using Elastic.Clients.Elasticsearch;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Search;

internal static class CatalogSearchDocumentFactory
{
    public static CatalogSearchDocument FromStore(StoreRow store, long publishedProducts, long publishedServices)
    {
        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var name = (store.Name ?? "").Trim();
        var cats = StoreSearchCategoryParser.ParseCategories(store.CategoriesJson);
        return new CatalogSearchDocument
        {
            Kind = CatalogSearchKinds.Store,
            StoreId = store.Id,
            OfferId = "",
            Name = name,
            VtCatalogSk = StoreSearchTextNormalize.FoldLowerKeyword(name),
            Categories = cats,
            SearchText = CatalogSearchEmbeddingText.ForStore(store),
            Location = location,
            VtLocation = location,
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
            SearchText = CatalogSearchEmbeddingText.ForProduct(p, store),
            Location = location,
            VtLocation = location,
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
            SearchText = CatalogSearchEmbeddingText.ForService(s, store),
            Location = location,
            VtLocation = location,
            TrustScore = store.TrustScore,
            PublishedProducts = pubProducts,
            PublishedServices = pubServices,
        };
    }
}
