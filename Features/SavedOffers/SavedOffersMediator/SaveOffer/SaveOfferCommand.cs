using MediatR;
using VibeTrade.Backend.Features.SavedOffers.Interfaces;

namespace VibeTrade.Backend.Features.SavedOffers.SavedOffersMediator.SaveOffer;

public sealed record SaveOfferCommand(string UserId, string ProductId)
    : IRequest<SaveOfferResult>;

public sealed record SaveOfferResult(SavedOfferMutationError Error, IReadOnlyList<string> SavedOfferIds);
