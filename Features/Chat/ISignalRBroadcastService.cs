namespace VibeTrade.Backend.Features.Chat;

/// <summary>Broadcasts SignalR para actualizaciones en tiempo real.</summary>
public interface ISignalRBroadcastService
{
    Task BroadcastCarrierTelemetryUpdatedAsync(
        string threadId,
        string routeSheetId,
        string agreementId,
        string routeStopId,
        string carrierUserId,
        double lat,
        double lng,
        double? progressFraction,
        bool offRoute,
        DateTimeOffset reportedAtUtc,
        double? speedKmh,
        string? avatarUrl,
        CancellationToken cancellationToken = default);

    Task BroadcastRouteTramoSubscriptionsChangedAsync(
        RouteTramoSubscriptionsBroadcastArgs request,
        CancellationToken cancellationToken = default);

    Task BroadcastOfferCommentsUpdatedAsync(string offerId, CancellationToken cancellationToken = default);
}
