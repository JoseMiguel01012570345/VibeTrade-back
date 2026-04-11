using System.Text.Json;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketStoreRowWorkspaceMapper
{
    public static void ApplyFields(JsonElement el, StoreRow row, DateTimeOffset now)
    {
        var ownerUserId = el.TryGetProperty("ownerUserId", out var ou) && ou.ValueKind == JsonValueKind.String
            ? ou.GetString()!
            : "unknown";
        row.OwnerUserId = ownerUserId;
        row.Name = MarketCatalogJsonHelpers.GetString(el, "name") ?? row.Name;
        row.NormalizedName = MarketStoreNameNormalizer.Normalize(row.Name);
        row.Verified = el.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
        row.TransportIncluded = el.TryGetProperty("transportIncluded", out var t) &&
                                t.ValueKind == JsonValueKind.True;
        row.TrustScore = el.TryGetProperty("trustScore", out var ts) && ts.TryGetInt32(out var ti) ? ti : row.TrustScore;
        row.AvatarUrl = MarketCatalogJsonHelpers.GetString(el, "avatarUrl");
        row.CategoriesJson = MarketCatalogJsonHelpers.SerializeStringArray(el, "categories");
        row.UpdatedAt = now;
        ApplyLocation(el, row);
    }

    public static void ApplyLocation(JsonElement storeEl, StoreRow row)
    {
        row.LocationLatitude = null;
        row.LocationLongitude = null;
        if (!storeEl.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Object)
            return;
        if (!loc.TryGetProperty("lat", out var latEl) || !latEl.TryGetDouble(out var lat))
            return;
        if (!loc.TryGetProperty("lng", out var lngEl) || !lngEl.TryGetDouble(out var lng))
            return;
        if (!double.IsFinite(lat) || lat < -90 || lat > 90)
            return;
        if (!double.IsFinite(lng) || lng < -180 || lng > 180)
            return;
        row.LocationLatitude = lat;
        row.LocationLongitude = lng;
    }
}
