namespace VibeTrade.Backend.Domain.Taxonomy;

/// <summary>Base para actores comerciales persistidos vía proyección DTO (extensible).</summary>
public abstract class CommercialActorBase : ICommercialListingOwner
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public int TrustScore { get; init; }
}
