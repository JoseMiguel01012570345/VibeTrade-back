using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapService(IMarketWorkspaceService marketWorkspace) : IBootstrapService
{
    public async Task<JsonDocument> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        using var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var marketObj = JsonNode.Parse(market.RootElement.GetRawText())!.AsObject();
        marketObj["storeCatalogs"] = new JsonObject();
        var m = marketObj.ToJsonString();
        const string reels =
            """{"items":[],"initialComments":{},"initialLikeCounts":{}}""";
        const string profileNames = "{}";
        var json =
            $"{{\"market\":{m},\"reels\":{reels},\"profileDisplayNames\":{profileNames}}}";
        return JsonDocument.Parse(json);
    }
}
