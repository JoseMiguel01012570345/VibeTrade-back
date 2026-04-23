using System.Text.Json.Nodes;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketCatalogStoreBadgeJson
{
    /// <summary>Coincide con <c>StoreBadge</c> en el cliente: evita ficha "no encontrada" si falta el row de tienda pero la oferta tiene <c>storeId</c>.</summary>
    public static JsonObject MinimalStub(string storeId)
    {
        return new JsonObject
        {
            ["id"] = storeId,
            ["name"] = "Tienda",
            ["verified"] = false,
            ["categories"] = new JsonArray(),
            ["transportIncluded"] = false,
            ["trustScore"] = 0,
        };
    }

    public static JsonObject FromStoreRow(StoreRow s)
    {
        var node = new JsonObject
        {
            ["id"] = s.Id,
            ["name"] = s.Name,
            ["verified"] = s.Verified,
            ["transportIncluded"] = s.TransportIncluded,
            ["trustScore"] = s.TrustScore,
            ["ownerUserId"] = s.OwnerUserId,
        };
        if (!string.IsNullOrEmpty(s.AvatarUrl))
            node["avatarUrl"] = s.AvatarUrl;
        try
        {
            node["categories"] = JsonNode.Parse(s.CategoriesJson) ?? new JsonArray();
        }
        catch
        {
            node["categories"] = new JsonArray();
        }

        if (s.LocationLatitude is { } la && s.LocationLongitude is { } lo)
            node["location"] = new JsonObject { ["lat"] = la, ["lng"] = lo };
        if (!string.IsNullOrWhiteSpace(s.Pitch))
            node["pitch"] = s.Pitch.Trim();
        if (!string.IsNullOrWhiteSpace(s.WebsiteUrl))
            node["websiteUrl"] = s.WebsiteUrl.Trim();
        return node;
    }
}
