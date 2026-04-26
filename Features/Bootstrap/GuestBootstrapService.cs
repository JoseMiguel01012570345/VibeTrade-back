using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Bootstrap;

public interface IGuestBootstrapService
{
    Task<BootstrapResponseDto> GetGuestBootstrapAsync(string guestId, CancellationToken cancellationToken = default);
}

public sealed class GuestBootstrapService(
    IMarketWorkspaceService marketWorkspace,
    IGuestRecommendationService recommendations) : IGuestBootstrapService
{
    public async Task<BootstrapResponseDto> GetGuestBootstrapAsync(string guestId, CancellationToken cancellationToken = default)
    {
        var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        market.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(StringComparer.Ordinal);
        market.Threads = new Dictionary<string, ChatThreadWorkspaceDto>(StringComparer.Ordinal);
        market.RouteOfferPublic = new Dictionary<string, RouteOfferPublicEntryView>(StringComparer.Ordinal);

        var recommendationFeed = await recommendations.GetBatchAsync(
            guestId,
            RecommendationService.DefaultBootstrapTake,
            cancellationToken);

        var bootRecOfferIds = recommendationFeed.OfferIds.Length > 0
            ? recommendationFeed.OfferIds
            : recommendationFeed.Offers.Keys.ToArray();
        if (bootRecOfferIds.Length > 0)
            market.OfferIds = new List<string>(bootRecOfferIds);

        return new BootstrapResponseDto
        {
            Market = market,
            Reels = new BootstrapReelsStateDto(),
            ProfileDisplayNames = new Dictionary<string, string>(),
            SavedOfferIds = Array.Empty<string>(),
            Recommendations = recommendationFeed,
        };
    }
}
