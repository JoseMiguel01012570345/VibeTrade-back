namespace VibeTrade.Backend.Features.Market.Taxonomy;

/// <summary>Entidad que puede poseer tiendas, ofertas y catálogo (taxonomía comercio).</summary>
public interface ICommercialListingOwner : ITrustRatedEntity
{
    string Id { get; }
    string DisplayName { get; }
}
