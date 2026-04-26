using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Market.Utils;

internal static class MarketStoreRowWorkspaceMapper
{
    public static void ApplyFields(StoreProfileWorkspaceData el, StoreRow row, DateTimeOffset now)
    {
        var d = el;
        var ownerUserId = string.IsNullOrWhiteSpace(d.OwnerUserId) ? "unknown" : d.OwnerUserId.Trim();
        row.OwnerUserId = ownerUserId;
        row.Name = d.Name?.Trim() ?? row.Name;
        row.NormalizedName = MarketStoreNameNormalizer.Normalize(row.Name);
        row.Verified = d.Verified == true;
        row.TransportIncluded = d.TransportIncluded == true;
        row.TrustScore = d.TrustScore ?? row.TrustScore;
        row.AvatarUrl = d.AvatarUrl;
        row.Categories = d.Categories is { Count: > 0 } cats
            ? cats.ToList()
            : new List<string>();
        row.UpdatedAt = now;
        if (d.Pitch is { } p)
            row.Pitch = p.Trim();
        row.WebsiteUrl = MarketWebsiteUrlNormalizer.TryNormalize(d.WebsiteUrl);
        ApplyLocation(d, row);
    }

    private static void ApplyLocation(StoreProfileWorkspaceData d, StoreRow row)
    {
        row.LocationLatitude = null;
        row.LocationLongitude = null;
        var loc = d.Location;
        if (loc is null) return;
        if (!double.IsFinite(loc.Lat) || loc.Lat < -90 || loc.Lat > 90)
            return;
        if (!double.IsFinite(loc.Lng) || loc.Lng < -180 || loc.Lng > 180)
            return;
        row.LocationLatitude = loc.Lat;
        row.LocationLongitude = loc.Lng;
    }
}
