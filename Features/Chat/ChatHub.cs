using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
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

    public async Task LeaveThread(string threadId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(threadId));
    }

    private static string ThreadGroup(string threadId) => $"thread:{threadId}";

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
