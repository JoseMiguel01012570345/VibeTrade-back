namespace VibeTrade.Backend.Features.Market.Interfaces;

/// <summary>Entidad que puede poseer tiendas, ofertas y catálogo (taxonomía comercio).</summary>
public interface ICommercialListingOwner : ITrustRatedEntity
{
    string Id { get; }
    string DisplayName { get; }
}
