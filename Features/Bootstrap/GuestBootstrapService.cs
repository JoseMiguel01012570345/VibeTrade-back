using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Bootstrap;

public interface IGuestBootstrapService
{
    Task<JsonDocument> GetGuestBootstrapAsync(string guestId, CancellationToken cancellationToken = default);
}

public sealed class GuestBootstrapService(
    IMarketWorkspaceService marketWorkspace,
    IGuestRecommendationService recommendations) : IGuestBootstrapService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<JsonDocument> GetGuestBootstrapAsync(string guestId, CancellationToken cancellationToken = default)
    {
        using var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var marketObj = JsonNode.Parse(market.RootElement.GetRawText())!.AsObject();

        // guest: no workspace privado
        marketObj["storeCatalogs"] = new JsonObject();
        marketObj["threads"] = new JsonObject();
        marketObj["routeOfferPublic"] = new JsonObject();

        var recommendationFeed = await recommendations.GetBatchAsync(
            guestId,
            RecommendationService.MaxBatchSize,
            cancellationToken);

        var bootRecOfferIds = recommendationFeed.OfferIds.Length > 0
            ? recommendationFeed.OfferIds
            : recommendationFeed.Offers.Select(kv => kv.Key).ToArray();
        marketObj["offerIds"] = JsonSerializer.SerializeToNode(bootRecOfferIds, JsonOptions) ?? new JsonArray();

        const string reels =
            """{"items":[],"initialComments":{},"initialLikeCounts":{}}""";
        const string profileNames = "{}";

        var root = new JsonObject
        {
            ["market"] = marketObj,
            ["reels"] = JsonNode.Parse(reels),
            ["profileDisplayNames"] = JsonNode.Parse(profileNames),
            ["savedOfferIds"] = new JsonArray(),
            ["recommendations"] = JsonSerializer.SerializeToNode(recommendationFeed, JsonOptions),
        };

        return JsonDocument.Parse(root.ToJsonString(JsonOptions));
    }
}

