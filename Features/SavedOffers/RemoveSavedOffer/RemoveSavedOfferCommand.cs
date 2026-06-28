using MediatR;

namespace VibeTrade.Backend.Features.SavedOffers.RemoveSavedOffer;

public sealed record RemoveSavedOfferCommand(string UserId, string ProductId)
    : IRequest<IReadOnlyList<string>?>;
