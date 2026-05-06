using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market.Interfaces;

public interface IOfferEngagementService
{
    Task EnrichHomeOffersAsync(
        Dictionary<string, HomeOfferViewDto> offers,
        string? likerKey,
        CancellationToken cancellationToken = default);

    /// <summary>Engagement (likes, conteo de comentarios) sobre filas de <see cref="StoreCatalogBlockView"/> (detalle de tienda).</summary>
    Task EnrichStoreCatalogBlockEngagementAsync(
        IReadOnlyList<StoreProductCatalogRowView> products,
        IReadOnlyList<StoreServiceCatalogRowView> services,
        string? likerKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfferQaItemResponseDto>> EnrichOfferQaListAsync(
        string offerId,
        IReadOnlyList<OfferQaComment> qa,
        string? likerKey,
        CancellationToken cancellationToken = default);

    Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(
        string offerId,
        string likerKey,
        CancellationToken cancellationToken = default);

    Task<(bool Liked, int LikeCount)> ToggleQaCommentLikeAsync(
        string offerId,
        string qaCommentId,
        string likerKey,
        CancellationToken cancellationToken = default);

    Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken = default);
}
