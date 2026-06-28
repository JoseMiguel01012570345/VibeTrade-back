namespace VibeTrade.Backend.Infrastructure.SignalR;

public interface ISignalRBroadcastAdapter
{
    Task SendToUserAsync(string userId, string method, object payload, CancellationToken cancellationToken = default);

    Task SendToThreadAsync(string threadId, string method, object payload, CancellationToken cancellationToken = default);

    Task SendToOfferAsync(string offerId, string method, object payload, CancellationToken cancellationToken = default);
}
