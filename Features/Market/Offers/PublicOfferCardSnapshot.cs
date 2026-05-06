namespace VibeTrade.Backend.Features.Market.Offers;

/// <summary>Ficha pública (<c>Offer</c> + tienda) para hidratar el cliente sin el feed completo.</summary>
public readonly record struct PublicOfferCardSnapshot(HomeOfferViewDto Offer, StoreProfileWorkspaceData Store);
