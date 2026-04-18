using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Realtime: grupos por hilo y por usuario. Autenticación con el mismo Bearer que la API.</summary>
public sealed class ChatHub(IAuthService auth, IServiceScopeFactory scopeFactory) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!TryGetUserId(out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    public async Task JoinThread(string threadId)
    {
        if (!TryGetUserId(out var userId))
            throw new HubException("Unauthorized");

        await using var scope = scopeFactory.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var t = await chat.GetThreadIfVisibleAsync(userId, threadId, Context.ConnectionAborted);
        if (t is null)
            throw new HubException("Forbidden");

        await Groups.AddToGroupAsync(Context.ConnectionId, ThreadGroup(threadId));
    }

    /// <summary>Solo deja el grupo del hilo (p. ej. al navegar fuera). Sin aviso a otros.</summary>
    public Task DisconnectFromThread(string threadId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(threadId));

    /// <summary>Recibe <c>offerCommentsUpdated</c> cuando alguien publica en la ficha (mismo grupo que <see cref="ChatService.BroadcastOfferCommentsUpdatedAsync"/>).</summary>
    public async Task JoinOffer(string offerId)
    {
        if (!TryGetUserId(out _))
            throw new HubException("Unauthorized");
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return;
        await Groups.AddToGroupAsync(Context.ConnectionId, OfferGroup(oid));
    }

    public Task LeaveOffer(string offerId)
    {
        if (!TryGetUserId(out _))
            return Task.CompletedTask;
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, OfferGroup(oid));
    }

    /// <summary>Aviso explícito «salir»: notifica al resto y luego desconecta del grupo.</summary>
    public async Task NotifyOthersUserLeftChat(string threadId)
    {
        if (!TryGetUserId(out var userId))
            throw new HubException("Unauthorized");

        await using var scope = scopeFactory.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var t = await chat.GetThreadIfVisibleAsync(userId, threadId, Context.ConnectionAborted);
        if (t is null)
            return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acc = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.PhoneDisplay, u.PhoneDigits })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        var displayName = acc is not null && !string.IsNullOrWhiteSpace(acc.DisplayName)
            ? acc.DisplayName.Trim()
            : (acc?.PhoneDisplay ?? acc?.PhoneDigits ?? userId);

        await Clients.GroupExcept(ThreadGroup(threadId), Context.ConnectionId).SendAsync(
            "participantLeft",
            new { threadId, userId, displayName },
            Context.ConnectionAborted);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(threadId));
    }

    private static string ThreadGroup(string threadId) => $"thread:{threadId}";

    private static string OfferGroup(string offerId) => $"offer:{offerId}";

    private static string UserGroup(string userId) => $"user:{userId}";

    private bool TryGetUserId(out string userId)
    {
        userId = "";
        var http = Context.GetHttpContext();
        if (http is null)
            return false;

        if (!http.Request.Headers.TryGetValue("Authorization", out var authz)
            || string.IsNullOrWhiteSpace(authz))
        {
            if (http.Request.Query.TryGetValue("access_token", out var q) && !string.IsNullOrWhiteSpace(q))
                authz = "Bearer " + q.ToString();
            else
                return false;
        }

        if (!auth.TryGetUserByToken(authz!, out var user))
            return false;

        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return false;

        userId = idEl.GetString() ?? "";
        return userId.Length > 0;
    }
}
