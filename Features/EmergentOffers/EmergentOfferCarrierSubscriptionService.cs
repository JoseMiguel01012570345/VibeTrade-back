using MediatR;
using VibeTrade.Backend.Features.EmergentOffers.GetCarrierSubscription;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;

namespace VibeTrade.Backend.Features.EmergentOffers;

public sealed class EmergentOfferCarrierSubscriptionService(IMediator mediator)
    : IEmergentOfferCarrierSubscriptionService
{
    public const string ReasonBuyerWithAcceptedAgreement = "buyer_with_accepted_agreement";

    public const string MessageBuyerWithAcceptedAgreementEs =
        "No puedes suscribirte como transportista: en esta operación eres el comprador con un acuerdo aceptado.";

    public Task<EmergentCarrierSubscriptionStatus> GetStatusAsync(
        string? viewerUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new GetCarrierSubscriptionQuery(viewerUserId, emergentOfferId), cancellationToken);
}
