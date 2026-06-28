using MediatR;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;

namespace VibeTrade.Backend.Features.EmergentOffers.EmergentOffersMediator.GetCarrierSubscription;

public sealed record GetCarrierSubscriptionQuery(string? ViewerUserId, string EmergentOfferId)
    : IRequest<EmergentCarrierSubscriptionStatus>;
