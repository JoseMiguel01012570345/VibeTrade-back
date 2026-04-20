using System.Text.Json.Nodes;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

public interface IOfferEngagementService
{
    /// <summary>Añade <c>publicCommentCount</c>, <c>offerLikeCount</c>, <c>viewerLikedOffer</c> a cada oferta.</summary>
    Task EnrichOffersJsonAsync(JsonObject offers, string? likerKey, CancellationToken cancellationToken = default);

    /// <summary>Array QA con <c>likeCount</c> y <c>viewerLiked</c> por ítem.</summary>
    Task<string?> EnrichOfferQaJsonAsync(string offerId, IReadOnlyList<OfferQaComment> qa, string? likerKey, CancellationToken cancellationToken = default);

    Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(string offerId, string likerKey, CancellationToken cancellationToken = default);

    Task<(bool Liked, int LikeCount)> ToggleQaCommentLikeAsync(
        string offerId,
        string qaCommentId,
        string likerKey,
        CancellationToken cancellationToken = default);

    Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken = default);
}
