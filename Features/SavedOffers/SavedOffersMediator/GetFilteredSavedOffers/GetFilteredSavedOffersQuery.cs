using MediatR;

namespace VibeTrade.Backend.Features.SavedOffers.SavedOffersMediator.GetFilteredSavedOffers;

public sealed record GetFilteredSavedOffersQuery(string ViewerUserId)
    : IRequest<IReadOnlyList<string>>;
