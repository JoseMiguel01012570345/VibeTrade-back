namespace VibeTrade.Backend.Features.SavedOffers.Dtos;

public sealed record SaveOfferBody(string ProductId);

public sealed record SavedOfferIdsResponse(IReadOnlyList<string> SavedOfferIds);
