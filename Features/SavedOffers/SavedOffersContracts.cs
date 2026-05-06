namespace VibeTrade.Backend.Features.SavedOffers;

public sealed record SaveOfferBody(string ProductId);

public sealed record SavedOfferIdsResponse(IReadOnlyList<string> SavedOfferIds);
