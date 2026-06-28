using MediatR;

namespace VibeTrade.Backend.Features.EmergentOffers.RequestRouteTramoSubscription;

public sealed record RequestRouteTramoSubscriptionCommand(
    string CarrierUserId,
    string EmergentOfferId,
    string StopId,
    string StoreServiceId) : IRequest<RequestRouteTramoSubscriptionResult>;

public sealed record RequestRouteTramoSubscriptionResult(bool Ok, string? ErrorCode, string? Message);
