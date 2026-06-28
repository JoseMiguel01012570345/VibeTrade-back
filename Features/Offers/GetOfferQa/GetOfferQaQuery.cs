using MediatR;
using VibeTrade.Backend.Features.Market.Dtos;

namespace VibeTrade.Backend.Features.Offers.GetOfferQa;

public sealed record GetOfferQaQuery(string OfferId, string? LikerKey)
    : IRequest<GetOfferQaResult>;

public sealed record GetOfferQaResult(
    IReadOnlyList<OfferQaItemResponseDto>? Qa,
    bool OfferFound);
