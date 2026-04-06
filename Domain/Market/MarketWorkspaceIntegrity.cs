using System.Text.Json;

namespace VibeTrade.Backend.Domain.Market;

public sealed class MarketWorkspaceIntegrity : IMarketWorkspaceIntegrity
{
    public void ValidateOrThrow(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Workspace must be a JSON object.");

        foreach (var key in new[] { "stores", "offers", "storeCatalogs", "threads", "routeOfferPublic" })
        {
            if (!root.TryGetProperty(key, out var p))
                throw new ArgumentException($"Workspace missing required property '{key}'.");
            if (p.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Workspace property '{key}' must be an object.");
        }

        if (!root.TryGetProperty("offerIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("offerIds must be an array.");
    }
}
