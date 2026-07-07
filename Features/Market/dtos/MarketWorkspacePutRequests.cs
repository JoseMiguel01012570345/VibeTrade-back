using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>
/// Ficha de tienda: JSON plano con <c>id</c> o anidado bajo <c>stores</c> (mismo contrato que el cliente de mercado).
/// </summary>
public sealed record WorkspaceStorePutRequest
{
    [JsonPropertyName("stores")]
    public Dictionary<string, StoreProfileWorkspaceData>? Stores { get; init; }

    public string? Id { get; init; }
    public string? Name { get; init; }
    public bool? Verified { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public bool? TransportIncluded { get; init; }
    public string? AvatarUrl { get; init; }
    public int? TrustScore { get; init; }
    public string? Pitch { get; init; }
    public string? OwnerUserId { get; init; }
    public StoreLocationPointBody? Location { get; init; }
    public string? WebsiteUrl { get; init; }
    public decimal? PricePerKm { get; init; }
    public string? PricePerKmCurrencyCode { get; init; }
}

/// <summary>Parche de workspace: <c>stores</c> y/o <c>storeCatalogs</c> por id de tienda.</summary>
public sealed record WorkspaceStoreCatalogsPutRequest
{
    [JsonPropertyName("stores")]
    public Dictionary<string, StoreProfileWorkspaceData>? Stores { get; init; }

    [JsonPropertyName("storeCatalogs")]
    public Dictionary<string, StoreCatalogBlockView>? StoreCatalogs { get; init; }
}
