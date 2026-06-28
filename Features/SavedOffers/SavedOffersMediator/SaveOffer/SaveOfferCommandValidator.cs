using FluentValidation;

namespace VibeTrade.Backend.Features.SavedOffers.SavedOffersMediator.SaveOffer;

public sealed class SaveOfferCommandValidator : AbstractValidator<SaveOfferCommand>
{
    public SaveOfferCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
