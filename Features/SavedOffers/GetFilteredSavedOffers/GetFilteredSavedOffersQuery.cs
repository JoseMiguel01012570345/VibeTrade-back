using MediatR;

namespace VibeTrade.Backend.Features.SavedOffers.GetFilteredSavedOffers;

public sealed record GetFilteredSavedOffersQuery(string ViewerUserId)
    : IRequest<IReadOnlyList<string>>;
