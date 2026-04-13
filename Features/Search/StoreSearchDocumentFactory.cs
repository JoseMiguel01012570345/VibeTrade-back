using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Search;

internal static class StoreSearchDocumentFactory
{
    public static async Task<StoreSearchDocument?> FromStoreAsync(
        AppDbContext db,
        StoreRow store,
        CancellationToken cancellationToken)
    {
        var publishedProducts = await db.StoreProducts.AsNoTracking()
            .CountAsync(p => p.StoreId == store.Id && p.Published, cancellationToken);
        var publishedServices = await db.StoreServices.AsNoTracking()
            .CountAsync(s => s.StoreId == store.Id && (s.Published == null || s.Published == true), cancellationToken);

        LatLonGeoLocation? location = null;
        if (store.LocationLatitude is { } la && store.LocationLongitude is { } lo)
            location = new LatLonGeoLocation { Lat = la, Lon = lo };

        var name = (store.Name ?? "").Trim();
        return new StoreSearchDocument
        {
            StoreId = store.Id,
            Name = name,
            NameSort = name.ToLowerInvariant(),
            Categories = StoreSearchCategoryParser.ParseCategories(store.CategoriesJson),
            Location = location,
            TrustScore = store.TrustScore,
            PublishedProducts = publishedProducts,
            PublishedServices = publishedServices,
        };
    }
}
