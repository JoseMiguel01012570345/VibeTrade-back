namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Ubicación de tienda en resultados de búsqueda (misma forma que la badge de catálogo).</summary>
public sealed record CatalogSearchStoreLocation(double Lat, double Lng);

/// <summary>
/// Ficha de tienda en búsqueda. Tipo explícito para que System.Text.Json serialice siempre <see cref="WebsiteUrl"/> y demás campos.
/// <see cref="CatalogSearchItem.Offer"/> serializa una de las variantes (producto, servicio o emergente) bajo la clave <c>offer</c>.
/// </summary>
public sealed record CatalogSearchStoreBadge(
    string Id,
    string Name,
    bool Verified,
    bool TransportIncluded,
    int TrustScore,
    string OwnerUserId,
    string? AvatarUrl,
    IReadOnlyList<string> Categories,
    CatalogSearchStoreLocation? Location,
    string? Pitch,
    string? WebsiteUrl);

public sealed record CatalogSearchItem(
    string Kind,
    CatalogSearchStoreBadge Store,
    CatalogSearchItemOffer? Offer,
    long? PublishedProducts,
    long? PublishedServices,
    double? DistanceKm);

public sealed record StoreSearchResponse(
    IReadOnlyList<CatalogSearchItem> Items,
    bool hasMore,
    int Offset,
    int Limit);

public sealed record StoreAutocompleteResponse(IReadOnlyList<string> Suggestions);
