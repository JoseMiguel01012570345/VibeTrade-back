using MediatR;

namespace VibeTrade.Backend.Features.Offers.OffersMediator.ToggleOfferLike;

public sealed record ToggleOfferLikeCommand(string OfferId, string LikerKey)
    : IRequest<ToggleOfferLikeResult>;

public sealed record ToggleOfferLikeResult(bool Liked, int LikeCount, bool OfferExists);
