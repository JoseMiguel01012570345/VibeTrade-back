using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Offers.Interfaces;

/// <summary>
/// Ofertas: vistas (home, catálogo, emergentes) y engagement (likes de oferta).
/// </summary>
public interface IOfferService
{
    Task EnrichHomeOffersAsync(
        Dictionary<string, HomeOfferViewDto> offers,
        string? likerKey,
        CancellationToken cancellationToken = default);

    Task EnrichStoreCatalogBlockEngagementAsync(
        IReadOnlyList<StoreProductCatalogRowView> products,
        IReadOnlyList<StoreServiceCatalogRowView> services,
        string? likerKey,
        CancellationToken cancellationToken = default);

    Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(
        string offerId,
        string likerKey,
        CancellationToken cancellationToken = default);

    Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken = default);

    HomeOfferViewDto FromProductRow(StoreProductRow p);

    HomeOfferViewDto FromServiceRow(StoreServiceRow s);

    StoreProductCatalogRowView ProductCatalogRowFromEntity(StoreProductRow p);

    StoreServiceCatalogRowView ServiceCatalogRowFromEntity(StoreServiceRow s);

    HomeOfferViewDto CreateEmergentRoutePublication(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId,
        RouteSheetPayload? liveRoutePayload = null);
}
