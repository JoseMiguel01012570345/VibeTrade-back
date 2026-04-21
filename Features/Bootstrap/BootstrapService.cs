using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.SavedOffers;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapService(
    IMarketWorkspaceService marketWorkspace,
    AppDbContext db,
    ISavedOffersService savedOffers,
    IRecommendationService recommendations,
    IChatService chat) : IBootstrapService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<JsonDocument> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default)
    {
        using var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var marketObj = JsonNode.Parse(market.RootElement.GetRawText())!.AsObject();

        var viewerDigits = new string(viewerPhoneDigits.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(viewerDigits))
            throw new ArgumentException("viewerPhoneDigits must contain digits.", nameof(viewerPhoneDigits));

        var viewerUser = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneDigits == viewerDigits, cancellationToken);

        // Filter stores by invariant phoneDigits (unique), not by ephemeral user.id.
        var storeIds = await db.Stores
            .AsNoTracking()
            .Where(s => s.Owner.PhoneDigits == viewerDigits)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var keepStoreIds = new HashSet<string>(storeIds, StringComparer.Ordinal);

        // stores + offers: datos globales del mercado (Home y exploración); no filtrar por dueño.
        // storeCatalogs: vacío en bootstrap (se hidrata con POST stores/:id/detail).
        marketObj["storeCatalogs"] = new JsonObject();

        // threads: solo hilos del workspace cuya tienda es del vendedor (demo). Los chats de comprador
        // usan storeId de la tienda ajena y se filtraban por completo → se pierden al recargar.
        // Abajo fusionamos hilos persistidos (cth_*) desde PostgreSQL.
        if (marketObj["threads"] is JsonObject threads)
        {
            var nextThreads = new JsonObject();
            foreach (var kv in threads)
            {
                if (kv.Value is not JsonObject th) continue;
                var storeId = th["storeId"]?.GetValue<string>();
                if (storeId is not null && keepStoreIds.Contains(storeId))
                    nextThreads[kv.Key] = JsonNode.Parse(th.ToJsonString());
            }
            marketObj["threads"] = nextThreads;

            // routeOfferPublic depends on threadId
            if (marketObj["routeOfferPublic"] is JsonObject rop)
            {
                var nextRop = new JsonObject();
                foreach (var kv in rop)
                {
                    if (kv.Value is not JsonObject v) continue;
                    var threadId = v["threadId"]?.GetValue<string>();
                    if (threadId is not null && nextThreads.ContainsKey(threadId))
                        nextRop[kv.Key] = JsonNode.Parse(v.ToJsonString());
                }
                marketObj["routeOfferPublic"] = nextRop;
            }
        }

        if (viewerUser is not null && marketObj["threads"] is JsonObject mergedThreads)
            await MergePersistedChatThreadsAsync(mergedThreads, marketObj, viewerUser.Id, cancellationToken);

        const string reels =
            """{"items":[],"initialComments":{},"initialLikeCounts":{}}""";
        const string profileNames = "{}";

        var savedList = viewerUser is null
            ? Array.Empty<string>()
            : (await savedOffers.GetFilteredForBootstrapAsync(viewerUser.Id, cancellationToken)).ToArray();
        var recommendationFeed = viewerUser is null
            ? RecommendationBatchResponse.Empty(RecommendationService.DefaultBatchSize, RecommendationService.ScoreThreshold)
            : await recommendations.GetBatchAsync(
                viewerUser.Id,
                RecommendationService.DefaultBatchSize,
                cancellationToken);

        var bootRecOfferIds = recommendationFeed.Offers.Select(kv => kv.Key).ToArray();
        marketObj["offerIds"] = JsonSerializer.SerializeToNode(bootRecOfferIds, JsonOptions) ?? new JsonArray();

        var root = new JsonObject
        {
            ["market"] = marketObj,
            ["reels"] = JsonNode.Parse(reels),
            ["profileDisplayNames"] = JsonNode.Parse(profileNames),
            ["savedOfferIds"] = JsonSerializer.SerializeToNode(savedList, JsonOptions) ?? new JsonArray(),
            ["recommendations"] = JsonSerializer.SerializeToNode(recommendationFeed, JsonOptions),
        };

        return JsonDocument.Parse(root.ToJsonString(JsonOptions));
    }

    /// <summary>
    /// Añade hilos <c>cth_*</c> del usuario (comprador o vendedor) con tienda/oferta desde tablas relacionales.
    /// </summary>
    private async Task MergePersistedChatThreadsAsync(
        JsonObject threadsOut,
        JsonObject marketObj,
        string viewerUserId,
        CancellationToken cancellationToken)
    {
        var summaries = await chat.ListThreadsForUserAsync(viewerUserId, cancellationToken);
        if (summaries.Count == 0)
            return;

        var stores = marketObj["stores"] as JsonObject ?? new JsonObject();
        marketObj["stores"] = stores;

        var offers = marketObj["offers"] as JsonObject ?? new JsonObject();
        marketObj["offers"] = offers;

        foreach (var summ in summaries)
        {
            if (threadsOut.ContainsKey(summ.Id))
                continue;

            var store = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == summ.StoreId, cancellationToken);
            if (store is null)
                continue;

            if (!stores.ContainsKey(store.Id))
                stores[store.Id] = MarketCatalogStoreBadgeJson.FromStoreRow(store);

            if (!offers.ContainsKey(summ.OfferId))
            {
                var product = await db.StoreProducts.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == summ.OfferId, cancellationToken);
                if (product is not null)
                    offers[summ.OfferId] = MarketCatalogOfferJsonBuilder.ProductRowToOfferJson(product);
                else
                {
                    var service = await db.StoreServices.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == summ.OfferId, cancellationToken);
                    if (service is not null)
                        offers[summ.OfferId] = MarketCatalogOfferJsonBuilder.ServiceRowToOfferJson(service);
                }
            }

            var storeNode = MarketCatalogStoreBadgeJson.FromStoreRow(store);
            var msgs = await chat.ListMessagesAsync(viewerUserId, summ.Id, cancellationToken);
            var messagesArr = new JsonArray();
            foreach (var m in msgs)
                messagesArr.Add(ChatMarketMessageJsonMapper.ToMarketMessage(m, viewerUserId));

            var thJson = new JsonObject
            {
                ["id"] = summ.Id,
                ["offerId"] = summ.OfferId,
                ["storeId"] = summ.StoreId,
                ["buyerUserId"] = summ.BuyerUserId,
                ["sellerUserId"] = summ.SellerUserId,
                ["store"] = storeNode,
                ["purchaseMode"] = summ.PurchaseMode,
                ["messages"] = messagesArr,
                ["contracts"] = new JsonArray(),
                ["routeSheets"] = new JsonArray(),
            };
            if (!string.IsNullOrWhiteSpace(summ.BuyerDisplayName))
                thJson["buyerDisplayName"] = summ.BuyerDisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(summ.BuyerAvatarUrl))
                thJson["buyerAvatarUrl"] = summ.BuyerAvatarUrl.Trim();
            threadsOut[summ.Id] = thJson;
        }
    }
}
