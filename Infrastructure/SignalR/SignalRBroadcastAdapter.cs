using Microsoft.AspNetCore.SignalR;
using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Infrastructure.SignalR;

public sealed class SignalRBroadcastAdapter(IHubContext<ChatHub> hub) : ISignalRBroadcastAdapter
{
    public Task SendToUserAsync(string userId, string method, object payload, CancellationToken cancellationToken = default)
    {
        var trimmed = (userId ?? "").Trim();
        if (trimmed.Length < 2)
            return Task.CompletedTask;
        return hub.Clients.Group(ChatHubGroupNames.ForUser(trimmed)).SendAsync(method, payload, cancellationToken);
    }

    public Task SendToThreadAsync(string threadId, string method, object payload, CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return Task.CompletedTask;
        return hub.Clients.Group(ChatHubGroupNames.ForThread(tid)).SendAsync(method, payload, cancellationToken);
    }

    public Task SendToOfferAsync(string offerId, string method, object payload, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Task.CompletedTask;
        return hub.Clients.Group(ChatHubGroupNames.ForOffer(oid)).SendAsync(method, payload, cancellationToken);
    }
}
