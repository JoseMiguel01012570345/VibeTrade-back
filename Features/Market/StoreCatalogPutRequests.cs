namespace VibeTrade.Backend.Features.Market;

/// <summary>Cuerpo del PUT de ficha de producto (misma forma que <c>StoreProduct</c> en el cliente).</summary>
public sealed record StoreProductPutRequest
{
    public required string Id { get; init; }
    public string? StoreId { get; init; }
    public string? Category { get; init; }
    public string? Name { get; init; }
    public string? Model { get; init; }
    public string? ShortDescription { get; init; }
    public string? MainBenefit { get; init; }
    public string? TechnicalSpecs { get; init; }
    public string? Condition { get; init; }
    public string? Price { get; init; }
    public string? MonedaPrecio { get; init; }
    public IReadOnlyList<string>? Monedas { get; init; }
    public string? Moneda { get; init; }
    public string? TaxesShippingInstall { get; init; }
    public bool? TransportIncluded { get; init; }
    public string? Availability { get; init; }
    public string? WarrantyReturn { get; init; }
    public string? ContentIncluded { get; init; }
    public string? UsageConditions { get; init; }
    public bool? Published { get; init; }
    public IReadOnlyList<string>? PhotoUrls { get; init; }
    public IReadOnlyList<StoreCustomFieldBody>? CustomFields { get; init; }
    public int? PublicCommentCount { get; init; }
    public int? OfferLikeCount { get; init; }
    public bool? ViewerLikedOffer { get; init; }
}

/// <summary>Cuerpo del PUT de ficha de servicio (misma forma que <c>StoreService</c> en el cliente).</summary>
public sealed record StoreServicePutRequest
{
    public required string Id { get; init; }
    public string? StoreId { get; init; }
    public bool? Published { get; init; }
    public string? Category { get; init; }
    public string? TipoServicio { get; init; }
    public IReadOnlyList<string>? Monedas { get; init; }
    public string? Moneda { get; init; }
    public string? Descripcion { get; init; }
    public ServiceRiesgosBody? Riesgos { get; init; }
    public string? Incluye { get; init; }
    public string? NoIncluye { get; init; }
    public ServiceDependenciasBody? Dependencias { get; init; }
    public string? Entregables { get; init; }
    public ServiceGarantiasBody? Garantias { get; init; }
    public string? PropIntelectual { get; init; }
    public IReadOnlyList<string>? PhotoUrls { get; init; }
    public IReadOnlyList<StoreCustomFieldBody>? CustomFields { get; init; }
    public int? PublicCommentCount { get; init; }
    public int? OfferLikeCount { get; init; }
    public bool? ViewerLikedOffer { get; init; }
}
