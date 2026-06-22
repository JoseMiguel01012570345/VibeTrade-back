namespace VibeTrade.Backend.Features.EmergentOffers.Dtos;

public sealed record CarrierSubscriptionResponse(
    bool CanSubscribe,
    string? ReasonCode,
    string? Message);

public sealed record TramoSubscriptionRequestBody(string StopId, string StoreServiceId);
