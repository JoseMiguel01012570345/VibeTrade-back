using MediatR;

namespace VibeTrade.Backend.Features.Offers.ToggleOfferLike;

public sealed record ToggleOfferLikeCommand(string OfferId, string LikerKey)
    : IRequest<ToggleOfferLikeResult>;

public sealed record ToggleOfferLikeResult(bool Liked, int LikeCount, bool OfferExists);
