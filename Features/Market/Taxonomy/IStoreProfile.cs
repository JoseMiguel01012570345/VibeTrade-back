namespace VibeTrade.Backend.Features.Market.Taxonomy;

/// <summary>Perfil de tienda / negocio en vitrina (flow-ui: perfil, catálogo).</summary>
public interface IStoreProfile : ICommercialListingOwner
{
    IReadOnlyList<string> Categories { get; }
    bool Verified { get; }
    bool TransportIncluded { get; }
}
