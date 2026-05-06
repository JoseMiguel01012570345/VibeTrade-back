using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Bootstrap.Dtos;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Core;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Popularity;
using VibeTrade.Backend.Features.Recommendations.Dtos;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Bootstrap;

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
