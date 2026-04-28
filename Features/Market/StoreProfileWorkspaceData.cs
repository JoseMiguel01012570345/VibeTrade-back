using System.Text.Json.Serialization;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Ficha de tienda en <c>workspace.stores[id]</c> (mismo shape que el cliente; solo campos leídos en persistencia).</summary>
public sealed class StoreProfileWorkspaceData
{
    public string? Id { get; set; }
    public string? OwnerUserId { get; set; }
    public string? Name { get; set; }
    public bool? Verified { get; set; }
    public IReadOnlyList<string>? Categories { get; set; }
    public bool? TransportIncluded { get; set; }
    public int? TrustScore { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Pitch { get; set; }
    public string? WebsiteUrl { get; set; }
    [JsonPropertyName("location")]
    public StoreLocationPointBody? Location { get; set; }

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

    /// <summary>Snapshot desde fila de tienda (lectura Home / detalle / bootstrap).</summary>
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
