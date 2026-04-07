using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketWorkspaceService(
    IMarketWorkspaceRepository repository,
    IMarketWorkspaceIntegrity integrity) : IMarketWorkspaceService
{
    /// <summary>Workspace mínimo válido cuando la tabla está vacía (sin archivos externos).</summary>
    private const string EmptyWorkspaceJson =
        """{"stores":{},"offers":{},"offerIds":[],"storeCatalogs":{},"threads":{},"routeOfferPublic":{}}""";

    public async Task<JsonDocument> GetOrSeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is not null) return existing;

        var seed = JsonDocument.Parse(EmptyWorkspaceJson);
        integrity.ValidateOrThrow(seed);
        await repository.SaveAsync(seed, cancellationToken);
        return seed;
    }

    public async Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        JsonDocument toSave;
        if (existing is null)
        {
            toSave = document;
        }
        else
        {
            toSave = MergeStoreCatalogsPreserve(existing, document);
            if (!ReferenceEquals(toSave, document))
                document.Dispose();
        }

        integrity.ValidateOrThrow(toSave);
        await repository.SaveAsync(toSave, cancellationToken);
        if (!ReferenceEquals(toSave, document))
            toSave.Dispose();
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

    public async Task<JsonDocument?> GetStoreDetailAsync(string storeId, CancellationToken cancellationToken = default)
    {
        using var doc = await GetOrSeedAsync(cancellationToken);
        var root = doc.RootElement;
        if (!root.GetProperty("stores").TryGetProperty(storeId, out var storeEl))
            return null;

        JsonElement catalogEl;
        if (root.GetProperty("storeCatalogs").TryGetProperty(storeId, out var cat))
            catalogEl = cat;
        else
            catalogEl = JsonSerializer.SerializeToElement(new
            {
                pitch = "",
                joinedAt = 0L,
                products = Array.Empty<object>(),
                services = Array.Empty<object>(),
            });

        var json = $"{{\"store\":{storeEl.GetRawText()},\"catalog\":{catalogEl.GetRawText()}}}";
        return JsonDocument.Parse(json);
    }
}
