using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketWorkspaceService(
    IMarketWorkspaceRepository repository,
    IMarketWorkspaceIntegrity integrity,
    IMarketCatalogSyncService catalog) : IMarketWorkspaceService
{
    /// <summary>Workspace mínimo válido cuando la tabla está vacía (sin archivos externos).</summary>
    private const string EmptyWorkspaceJson =
        """{"stores":{},"offers":{},"offerIds":[],"storeCatalogs":{},"threads":{},"routeOfferPublic":{}}""";

    public async Task<JsonDocument> GetOrSeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is null)
        {
            var seed = JsonDocument.Parse(EmptyWorkspaceJson);
            integrity.ValidateOrThrow(seed);
            await repository.SaveAsync(seed, cancellationToken);
            seed.Dispose();
            existing = await repository.GetAsync(cancellationToken);
        }

        if (existing is null)
            throw new InvalidOperationException("Market workspace row is missing after seed.");

        var root = JsonNode.Parse(existing.RootElement.GetRawText())!.AsObject();
        existing.Dispose();

        root["stores"] = await catalog.BuildStoresJsonObjectAsync(cancellationToken);
        root["storeCatalogs"] = await catalog.BuildStoreCatalogsJsonObjectAsync(cancellationToken);

        var (offers, offerIds) = await catalog.BuildPublishedOffersFeedAsync(cancellationToken);
        root["offers"] = offers;
        root["offerIds"] = offerIds;

        return JsonDocument.Parse(root.ToJsonString());
    }

    /// <summary>
    /// Fusiona un parche parcial del cliente sobre el workspace persistido (por id en stores, storeCatalogs, offers, etc.).
    /// </summary>
    private static JsonDocument MergeWorkspacePatchIntoExisting(JsonDocument existingFull, JsonDocument incoming)
    {
        var exRoot = JsonNode.Parse(existingFull.RootElement.GetRawText())!.AsObject();
        var inRoot = JsonNode.Parse(incoming.RootElement.GetRawText())!.AsObject();
        EnsureWorkspaceShape(exRoot);

        foreach (var kv in inRoot)
        {
            switch (kv.Key)
            {
                case "stores":
                case "storeCatalogs":
                case "offers":
                case "threads":
                case "routeOfferPublic":
                    MergeJsonObjectChildren(exRoot, kv.Key, kv.Value);
                    break;
                case "offerIds":
                    if (kv.Value is JsonArray arr)
                        exRoot["offerIds"] = JsonNode.Parse(arr.ToJsonString())!;
                    break;
            }
        }

        return JsonDocument.Parse(exRoot.ToJsonString());
    }

    private static void EnsureWorkspaceShape(JsonObject root)
    {
        if (root["stores"] is not JsonObject)
            root["stores"] = new JsonObject();
        if (root["offers"] is not JsonObject)
            root["offers"] = new JsonObject();
        if (root["storeCatalogs"] is not JsonObject)
            root["storeCatalogs"] = new JsonObject();
        if (root["threads"] is not JsonObject)
            root["threads"] = new JsonObject();
        if (root["routeOfferPublic"] is not JsonObject)
            root["routeOfferPublic"] = new JsonObject();
        if (root["offerIds"] is not JsonArray)
            root["offerIds"] = new JsonArray();
    }

    private static void MergeJsonObjectChildren(JsonObject exRoot, string propertyName, JsonNode? incomingNode)
    {
        if (incomingNode is not JsonObject inObj)
            return;
        if (exRoot[propertyName] is not JsonObject exObj)
        {
            exObj = new JsonObject();
            exRoot[propertyName] = exObj;
        }

        foreach (var kv in inObj)
        {
            exObj[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
        }
    }

    public Task<JsonDocument?> GetStoreDetailAsync(string storeId, CancellationToken cancellationToken = default) =>
        catalog.GetStoreDetailDocumentAsync(storeId, cancellationToken);

    public async Task<JsonDocument> GetStoresSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var o = await catalog.BuildStoresJsonObjectAsync(cancellationToken);
        var root = new JsonObject { ["stores"] = o };
        return JsonDocument.Parse(root.ToJsonString());
    }

    public async Task SaveStoreProfilesAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        await SavePartialAsync(
            document,
            static (c, el, ct) => c.ApplyStoreProfilesFromWorkspaceAsync(el, ct),
            cancellationToken);
    }

    public async Task SaveStoreCatalogsAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        await SavePartialAsync(
            document,
            static (c, el, ct) => c.ApplyStoreCatalogsFromWorkspaceAsync(el, ct),
            cancellationToken);
    }

    public async Task SaveOfferInquiriesAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        await SavePartialAsync(
            document,
            static (c, el, ct) => c.ApplyOfferInquiriesFromWorkspaceAsync(el, ct),
            cancellationToken);
    }

    private async Task SavePartialAsync(
        JsonDocument document,
        Func<IMarketCatalogSyncService, JsonElement, CancellationToken, Task> applyRelational,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(cancellationToken);
        JsonDocument merged;
        if (existing is null)
        {
            using var seed = JsonDocument.Parse(EmptyWorkspaceJson);
            merged = MergeWorkspacePatchIntoExisting(seed, document);
        }
        else
        {
            merged = MergeWorkspacePatchIntoExisting(existing, document);
            existing.Dispose();
        }

        if (!ReferenceEquals(merged, document))
            document.Dispose();

        await applyRelational(catalog, merged.RootElement, cancellationToken);

        var slimRoot = JsonNode.Parse(merged.RootElement.GetRawText())!.AsObject();
        slimRoot["stores"] = new JsonObject();
        slimRoot["storeCatalogs"] = new JsonObject();

        merged.Dispose();

        using var slimDoc = JsonDocument.Parse(slimRoot.ToJsonString());
        integrity.ValidateOrThrow(slimDoc);
        await repository.SaveAsync(slimDoc, cancellationToken);
    }
}
