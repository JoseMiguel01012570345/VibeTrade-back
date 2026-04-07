using System.Text.Json;
using System.Text.Json.Nodes;
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

        return JsonDocument.Parse(root.ToJsonString());
    }

    public async Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        JsonDocument merged;
        var ownsMerged = false;
        if (existing is null)
        {
            merged = document;
        }
        else
        {
            merged = MergeStoreCatalogsPreserve(existing, document);
            ownsMerged = !ReferenceEquals(merged, document);
            if (ownsMerged)
                document.Dispose();
            existing.Dispose();
        }

        await catalog.ApplyStoresAndCatalogsFromWorkspaceAsync(merged.RootElement, cancellationToken);

        var slimRoot = JsonNode.Parse(merged.RootElement.GetRawText())!.AsObject();
        slimRoot["stores"] = new JsonObject();
        slimRoot["storeCatalogs"] = new JsonObject();

        if (ownsMerged)
            merged.Dispose();

        using var slimDoc = JsonDocument.Parse(slimRoot.ToJsonString());
        integrity.ValidateOrThrow(slimDoc);
        await repository.SaveAsync(slimDoc, cancellationToken);
    }

    /// <summary>
    /// El cliente puede enviar solo los catálogos ya hidratados; conservamos el resto desde BD para no borrar datos.
    /// </summary>
    private static JsonDocument MergeStoreCatalogsPreserve(JsonDocument existingFull, JsonDocument incoming)
    {
        var exRoot = JsonNode.Parse(existingFull.RootElement.GetRawText())!.AsObject();
        var inRoot = JsonNode.Parse(incoming.RootElement.GetRawText())!.AsObject();
        if (exRoot["storeCatalogs"] is JsonObject exCat && inRoot["storeCatalogs"] is JsonObject inCat)
        {
            foreach (var kv in exCat)
            {
                if (inCat.ContainsKey(kv.Key))
                    continue;
                inCat[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
            }
        }

        return JsonDocument.Parse(inRoot.ToJsonString());
    }

    public Task<JsonDocument?> GetStoreDetailAsync(string storeId, CancellationToken cancellationToken = default) =>
        catalog.GetStoreDetailDocumentAsync(storeId, cancellationToken);
}
