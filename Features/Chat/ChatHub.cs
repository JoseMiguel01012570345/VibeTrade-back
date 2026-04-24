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
        var http = Context.GetHttpContext();
        if (http is null || !TryGetBearerHeader(http, out var bearerHeader))
            throw new HubException("Unauthorized");

        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            throw new HubException("Forbidden");

        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedAuth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        if (!scopedAuth.TryGetUserByToken(bearerHeader, out var userEl))
            throw new HubException("Unauthorized");
        if (!userEl.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            throw new HubException("Unauthorized");
        var userId = (idEl.GetString() ?? "").Trim();
        if (userId.Length == 0)
            throw new HubException("Unauthorized");

        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var t = await chat.GetThreadIfVisibleAsync(userId, tid, Context.ConnectionAborted);
        if (t is null)
            throw new HubException("Forbidden");

        // Mismo nombre de grupo que <see cref="ChatService"/> (<c>thread:{tid}</c> con tid recortado).
        await Groups.AddToGroupAsync(Context.ConnectionId, ThreadGroup(tid));
    }

    /// <summary>Solo deja el grupo del hilo (p. ej. al navegar fuera). Sin aviso a otros.</summary>
    public Task DisconnectFromThread(string threadId)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(tid));
    }

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
        var http = Context.GetHttpContext();
        if (http is null || !TryGetBearerHeader(http, out var bearerHeader))
            throw new HubException("Unauthorized");

        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedAuth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        if (!scopedAuth.TryGetUserByToken(bearerHeader, out var userEl))
            throw new HubException("Unauthorized");
        if (!userEl.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            throw new HubException("Unauthorized");
        var userId = (idEl.GetString() ?? "").Trim();
        if (userId.Length == 0)
            throw new HubException("Unauthorized");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var t = await chat.GetThreadIfVisibleAsync(userId, tid, Context.ConnectionAborted);
        if (t is null)
        {
            var threadOk = await db.ChatThreads.AsNoTracking()
                .AnyAsync(x => x.Id == tid && x.DeletedAtUtc == null, Context.ConnectionAborted);
            if (!threadOk)
                return;
            var wasCarrierOnThread = await db.RouteTramoSubscriptions.AsNoTracking()
                .AnyAsync(x => x.ThreadId == tid && x.CarrierUserId == userId, Context.ConnectionAborted);
            if (!wasCarrierOnThread)
                return;
        }
        var acc = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.PhoneDisplay, u.PhoneDigits })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        var displayName = acc is not null && !string.IsNullOrWhiteSpace(acc.DisplayName)
            ? acc.DisplayName.Trim()
            : (acc?.PhoneDisplay ?? acc?.PhoneDigits ?? userId);

        await Clients.GroupExcept(ThreadGroup(tid), Context.ConnectionId).SendAsync(
            "participantLeft",
            new { threadId = tid, userId, displayName },
            Context.ConnectionAborted);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(tid));
    }

    private static string ThreadGroup(string threadId) => $"thread:{threadId}";

    private static string OfferGroup(string offerId) => $"offer:{offerId}";

    private static string UserGroup(string userId) => $"user:{userId}";

    private static bool TryGetBearerHeader(HttpContext http, out string bearerHeader)
    {
        bearerHeader = "";
        if (http.Request.Headers.TryGetValue("Authorization", out var authz)
            && !string.IsNullOrWhiteSpace(authz))
        {
            bearerHeader = authz.ToString().Trim();
            return bearerHeader.Length > 0;
        }

        if (http.Request.Query.TryGetValue("access_token", out var q) && !string.IsNullOrWhiteSpace(q))
        {
            var token = q.ToString().Trim();
            if (token.Length == 0)
                return false;
            bearerHeader = "Bearer " + token;
            return true;
        }

        return false;
    }

    private bool TryGetUserId(out string userId)
    {
        userId = "";
        var http = Context.GetHttpContext();
        if (http is null || !TryGetBearerHeader(http, out var bearerHeader))
            return false;

        if (!auth.TryGetUserByToken(bearerHeader, out var user))
            return false;

        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return false;

        userId = (idEl.GetString() ?? "").Trim();
        return userId.Length > 0;
    }
}
