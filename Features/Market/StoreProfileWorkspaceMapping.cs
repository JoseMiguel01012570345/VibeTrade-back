
namespace VibeTrade.Backend.Features.Market;

/// <summary>Mapeos de filas de tienda al workspace persistido.</summary>
public static class StoreProfileWorkspaceMapping
{
    public static StoreProfileWorkspaceData MinimalStub(string storeId) =>
        new()
        {
            Id = storeId,
            Name = "Tienda",
            Verified = false,
            TransportIncluded = false,
            TrustScore = 0,
            OwnerUserId = "",
            Categories = Array.Empty<string>(),
        };

    public static StoreProfileWorkspaceData FromStoreRow(StoreRow s) =>
        new()
        {
            Id = s.Id,
            Name = s.Name,
            OwnerUserId = s.OwnerUserId,
            Verified = s.Verified,
            TransportIncluded = s.TransportIncluded,
            TrustScore = s.TrustScore,
            AvatarUrl = string.IsNullOrEmpty(s.AvatarUrl) ? null : s.AvatarUrl,
            Categories = CatalogJsonColumnParsing.StringListOrEmpty(s.Categories).ToList(),
            Pitch = string.IsNullOrWhiteSpace(s.Pitch) ? null : s.Pitch.Trim(),
            WebsiteUrl = string.IsNullOrWhiteSpace(s.WebsiteUrl) ? null : s.WebsiteUrl.Trim(),
            Location = s.LocationLatitude is { } la && s.LocationLongitude is { } lo
                ? new StoreLocationPointBody { Lat = la, Lng = lo }
                : null,
        };
}
