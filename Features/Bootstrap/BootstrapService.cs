using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Interfaces;
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
using VibeTrade.Backend.Features.SavedOffers;
using VibeTrade.Backend.Features.SavedOffers.Interfaces;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapService(
    IMarketWorkspaceService marketWorkspace,
    AppDbContext db,
    ISavedOffersService savedOffers,
    IRecommendationService recommendations,
    IChatService chat,
    IRouteSheetChatService routeSheets) : IBootstrapService
{
    public async Task<BootstrapResponseDto> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default)
    {
        var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);

        var viewerDigits = new string(viewerPhoneDigits.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(viewerDigits))
            throw new ArgumentException("viewerPhoneDigits must contain digits.", nameof(viewerPhoneDigits));

        var viewerUser = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneDigits == viewerDigits, cancellationToken);

        var storeIds = await db.Stores
            .AsNoTracking()
            .Where(s => s.Owner.PhoneDigits == viewerDigits)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var keepStoreIds = new HashSet<string>(storeIds, StringComparer.Ordinal);

        market.StoreCatalogs = new Dictionary<string, StoreCatalogBlockView>(StringComparer.Ordinal);

        if (market.Threads.Count > 0)
        {
            var nextThreads = new Dictionary<string, ChatThreadWorkspaceDto>(StringComparer.Ordinal);
            foreach (var kv in market.Threads)
            {
                var th = kv.Value;
                if (keepStoreIds.Contains(th.StoreId))
                    nextThreads[kv.Key] = th;
            }
            market.Threads = nextThreads;

            if (market.RouteOfferPublic.Count > 0)
            {
                var nextRop = new Dictionary<string, RouteOfferPublicEntryView>(StringComparer.Ordinal);
                foreach (var kv in market.RouteOfferPublic)
                {
                    var threadId = kv.Value.ThreadId;
                    if (threadId is not null && nextThreads.ContainsKey(threadId))
                        nextRop[kv.Key] = kv.Value;
                }
                market.RouteOfferPublic = nextRop;
            }
        }

        if (viewerUser is not null)
            await MergePersistedChatThreadsAsync(market, viewerUser.Id, cancellationToken);

        var savedList = viewerUser is null
            ? Array.Empty<string>()
            : (await savedOffers.GetFilteredForBootstrapAsync(viewerUser.Id, cancellationToken)).ToArray();
        var recommendationFeed = viewerUser is null
            ? RecommendationBatchResponse.Empty(RecommendationService.DefaultBatchSize, RecommendationService.ScoreThreshold)
            : await recommendations.GetBatchAsync(
                viewerUser.Id,
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
            SavedOfferIds = savedList,
            Recommendations = recommendationFeed,
        };
    }

    private async Task MergePersistedChatThreadsAsync(
        MarketWorkspaceState market,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var threadsOut = market.Threads;
        var summaries = await chat.ListThreadsForUserAsync(viewerUserId, cancellationToken);
        if (summaries.Count == 0)
            return;

        var stores = market.Stores;
        var offers = market.Offers;

        foreach (var summ in summaries)
        {
            if (threadsOut.TryGetValue(summ.Id, out var existing))
            {
                var rsExisting = await routeSheets.ListForThreadAsync(viewerUserId, summ.Id, cancellationToken);
                if (rsExisting is { Count: > 0 })
                    existing.RouteSheets = rsExisting;

                if (!string.IsNullOrWhiteSpace(summ.PartyExitedUserId))
                    existing.PartyExitedUserId = summ.PartyExitedUserId.Trim();
                else
                    existing.PartyExitedUserId = null;
                if (!string.IsNullOrWhiteSpace(summ.PartyExitedReason))
                    existing.PartyExitedReason = summ.PartyExitedReason.Trim();
                else
                    existing.PartyExitedReason = null;
                if (summ.PartyExitedAtUtc is { } partyAt)
                    existing.PartyExitedAtUtc = partyAt;
                else
                    existing.PartyExitedAtUtc = null;
                existing.IsSocialGroup = summ.IsSocialGroup;
                existing.SocialGroupTitle = string.IsNullOrWhiteSpace(summ.SocialGroupTitle)
                    ? null
                    : summ.SocialGroupTitle.Trim();
                continue;
            }

            var store = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == summ.StoreId, cancellationToken);
            if (store is null)
                continue;

            if (!stores.ContainsKey(store.Id))
                stores[store.Id] = StoreProfileWorkspaceData.FromStoreRow(store);

            if (!offers.ContainsKey(summ.OfferId))
            {
                var product = await db.StoreProducts.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == summ.OfferId, cancellationToken);
                if (product is not null)
                    offers[summ.OfferId] = HomeOfferViewFactory.FromProductRow(product);
                else
                {
                    var service = await db.StoreServices.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == summ.OfferId, cancellationToken);
                    if (service is not null)
                        offers[summ.OfferId] = HomeOfferViewFactory.FromServiceRow(service);
                }
            }

            var storeData = StoreProfileWorkspaceData.FromStoreRow(store);
            var msgs = await chat.ListMessagesAsync(viewerUserId, summ.Id, cancellationToken);
            var messages = msgs.Select(m => ChatMarketMessageJsonMapper.ToMarketMessage(m, viewerUserId)).ToList();

            var routeSheetsList = await routeSheets.ListForThreadAsync(viewerUserId, summ.Id, cancellationToken)
                ?? Array.Empty<RouteSheetPayload>();

            var th = new ChatThreadWorkspaceDto
            {
                Id = summ.Id,
                OfferId = summ.OfferId,
                StoreId = summ.StoreId,
                BuyerUserId = summ.BuyerUserId,
                SellerUserId = summ.SellerUserId,
                Store = storeData,
                PurchaseMode = summ.PurchaseMode,
                Messages = messages,
                Contracts = new List<ChatThreadContractView>(),
                RouteSheets = routeSheetsList,
                IsSocialGroup = summ.IsSocialGroup,
                SocialGroupTitle = string.IsNullOrWhiteSpace(summ.SocialGroupTitle)
                    ? null
                    : summ.SocialGroupTitle.Trim(),
            };
            if (!string.IsNullOrWhiteSpace(summ.BuyerDisplayName))
                th.BuyerDisplayName = summ.BuyerDisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(summ.BuyerAvatarUrl))
                th.BuyerAvatarUrl = summ.BuyerAvatarUrl.Trim();
            if (!string.IsNullOrWhiteSpace(summ.PartyExitedUserId))
                th.PartyExitedUserId = summ.PartyExitedUserId.Trim();
            if (!string.IsNullOrWhiteSpace(summ.PartyExitedReason))
                th.PartyExitedReason = summ.PartyExitedReason.Trim();
            if (summ.PartyExitedAtUtc is { } pAt)
                th.PartyExitedAtUtc = pAt;
            threadsOut[summ.Id] = th;
        }
    }
}
