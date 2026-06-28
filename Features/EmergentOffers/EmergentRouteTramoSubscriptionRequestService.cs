using MediatR;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;
using VibeTrade.Backend.Features.EmergentOffers.EmergentOffersMediator.RequestRouteTramoSubscription;

namespace VibeTrade.Backend.Features.EmergentOffers;

public sealed class EmergentRouteTramoSubscriptionRequestService(IMediator mediator)
    : IEmergentRouteTramoSubscriptionRequestService
{
    public const string ErrInvalidEmergent = "invalid_emergent_offer";
    public const string ErrInvalidStop = "invalid_stop";
    public const string ErrNotPublished = "route_not_published";
    public const string ErrStopDelivered = "stop_delivered";
    public const string ErrInvalidService = "invalid_transport_service";
    public const string ErrServiceNotTransport = "service_not_transport";

    public async Task<(bool Ok, string? ErrorCode, string? Message)> RequestAsync(
        string carrierUserId,
        string emergentOfferId,
        string stopId,
        string storeServiceId,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new RequestRouteTramoSubscriptionCommand(carrierUserId, emergentOfferId, stopId, storeServiceId),
            cancellationToken);
        return (result.Ok, result.ErrorCode, result.Message);
    }
}
