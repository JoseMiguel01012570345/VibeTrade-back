using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Market.Utils;

public static class MarketWorkspaceStoresPutBodyNormalizer
{
    /// <summary>
    /// Acepta <c>{"id":"...","name":...}</c> y lo convierte al patch interno <c>stores[id]</c>.
    /// </summary>
    public static JsonDocument Normalize(JsonDocument body)
    {
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Root must be an object.", nameof(body));

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var storeId = idEl.GetString();
            if (string.IsNullOrWhiteSpace(storeId))
                throw new ArgumentException("Store id is empty.", nameof(body));

            var wrapped = new JsonObject
            {
                ["stores"] = new JsonObject { [storeId] = JsonNode.Parse(root.GetRawText())! },
            };
            var doc = JsonDocument.Parse(wrapped.ToJsonString());
            body.Dispose();
            return doc;
        }

        throw new ArgumentException("Missing stores object or store id.", nameof(body));
    }
}
