using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapService(IMarketWorkspaceService marketWorkspace, AppDbContext db) : IBootstrapService
{
    public async Task<JsonDocument> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default)
    {
        using var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var marketObj = JsonNode.Parse(market.RootElement.GetRawText())!.AsObject();

        var viewerDigits = new string(viewerPhoneDigits.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(viewerDigits))
            throw new ArgumentException("viewerPhoneDigits must contain digits.", nameof(viewerPhoneDigits));

        // Filter stores by invariant phoneDigits (unique), not by ephemeral user.id.
        var storeIds = await db.Stores
            .AsNoTracking()
            .Where(s => s.Owner.PhoneDigits == viewerDigits)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var keepStoreIds = new HashSet<string>(storeIds, StringComparer.Ordinal);

        // stores
        if (marketObj["stores"] is JsonObject stores)
        {
            var filtered = new JsonObject();
            foreach (var id in keepStoreIds)
            {
                if (stores[id] is null) continue;
                filtered[id] = JsonNode.Parse(stores[id]!.ToJsonString());
            }
            marketObj["stores"] = filtered;
        }

        // storeCatalogs: keep empty for bootstrap (hydrated on demand)
        marketObj["storeCatalogs"] = new JsonObject();

        // offers + offerIds
        if (marketObj["offers"] is JsonObject offers)
        {
            var nextOffers = new JsonObject();
            foreach (var kv in offers)
            {
                if (kv.Value is not JsonObject offer) continue;
                var storeId = offer["storeId"]?.GetValue<string>();
                if (storeId is not null && keepStoreIds.Contains(storeId))
                    nextOffers[kv.Key] = JsonNode.Parse(offer.ToJsonString());
            }
            marketObj["offers"] = nextOffers;
            marketObj["offerIds"] = new JsonArray(
                nextOffers.Select(kv => (JsonNode?)JsonValue.Create(kv.Key)).ToArray());
        }

        // threads
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

        var m = marketObj.ToJsonString();
        const string reels =
            """{"items":[],"initialComments":{},"initialLikeCounts":{}}""";
        const string profileNames = "{}";
        var json =
            $"{{\"market\":{m},\"reels\":{reels},\"profileDisplayNames\":{profileNames}}}";
        return JsonDocument.Parse(json);
    }
}
