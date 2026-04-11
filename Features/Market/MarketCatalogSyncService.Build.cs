using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService
{
    public async Task<JsonObject> BuildStoresJsonObjectAsync(CancellationToken cancellationToken = default)
    {
        var o = new JsonObject();
        var list = await db.Stores.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var s in list)
            o[s.Id] = MarketCatalogStoreBadgeJson.FromStoreRow(s);
        return o;
    }

    public async Task<JsonObject> BuildStoreCatalogsJsonObjectAsync(CancellationToken cancellationToken = default)
    {
        var root = new JsonObject();
        var storeIds = await db.Stores.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken);
        foreach (var storeId in storeIds)
        {
            var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == storeId, cancellationToken);
            var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

            root[storeId] = new JsonObject
            {
                ["pitch"] = store.Pitch,
                ["joinedAt"] = store.JoinedAtMs,
                ["products"] = new JsonArray(products.Select(MarketCatalogRowJsonSerialization.ProductToJson).ToArray<JsonNode?>()),
                ["services"] = new JsonArray(services.Select(MarketCatalogRowJsonSerialization.ServiceToJson).ToArray<JsonNode?>()),
            };
        }

        return root;
    }

    public async Task<JsonDocument?> GetStoreDetailDocumentAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        if (store is null)
            return null;

        var products = await db.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId).ToListAsync(cancellationToken);
        var services = await db.StoreServices.AsNoTracking().Where(s => s.StoreId == storeId).ToListAsync(cancellationToken);

        var catalog = new JsonObject
        {
            ["pitch"] = store.Pitch,
            ["joinedAt"] = store.JoinedAtMs,
            ["products"] = new JsonArray(products.Select(MarketCatalogRowJsonSerialization.ProductToJson).ToArray<JsonNode?>()),
            ["services"] = new JsonArray(services.Select(MarketCatalogRowJsonSerialization.ServiceToJson).ToArray<JsonNode?>()),
        };

        var root = new JsonObject { ["store"] = MarketCatalogStoreBadgeJson.FromStoreRow(store), ["catalog"] = catalog };
        return JsonDocument.Parse(root.ToJsonString());
    }
}
