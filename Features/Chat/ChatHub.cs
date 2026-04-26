using Microsoft.AspNetCore.SignalR;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat.Utils;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Realtime: grupos por hilo y por usuario. Autenticación con el mismo Bearer que la API.</summary>
public sealed class ChatHub(IAuthService auth, IServiceScopeFactory scopeFactory) : Hub
{
    /// <summary>Id de usuario fijado en <see cref="OnConnectedAsync"/>; en WebSockets el <c>HttpContext</c>
    /// de invocaciones posteriores a veces no reexpone <c>access_token</c> como en el negotiate.</summary>
    private const string CtxUserId = "__vtUserId";

    public override async Task OnConnectedAsync()
    {
        if (!TryReadBearerUserId(out var userId))
        {
            Context.Abort();
            return;
        }

        Context.Items[CtxUserId] = userId;
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    public async Task JoinThread(string threadId)
    {
        var userId = await GetConnectionUserIdForHubCallAsync(
            Context.ConnectionAborted, migrateOnUserIdChange: true);
        if (string.IsNullOrEmpty(userId))
            throw new HubException("Unauthorized");

        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            throw new HubException("Forbidden");

        await using var scope = scopeFactory.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var t = await chat.GetThreadIfVisibleAsync(userId, tid, Context.ConnectionAborted);
        if (t is null)
            throw new HubException("Forbidden");

        // Mismo nombre de grupo que <see cref="ChatService"/> (ver <see cref="ChatHubGroupNames.ForThread"/>).
        await Groups.AddToGroupAsync(Context.ConnectionId, ChatHubGroupNames.ForThread(tid));
    }

    /// <summary>Solo deja el grupo del hilo (p. ej. al navegar fuera). Sin aviso a otros.</summary>
    public Task DisconnectFromThread(string threadId)
    {
        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            ChatHubGroupNames.ForThread(tid));
    }

    /// <summary>Recibe <c>offerCommentsUpdated</c> cuando alguien publica en la ficha (mismo grupo que <see cref="ChatService.BroadcastOfferCommentsUpdatedAsync"/>).</summary>
    public async Task JoinOffer(string offerId)
    {
        if (string.IsNullOrEmpty(await GetConnectionUserIdForHubCallAsync(
                Context.ConnectionAborted, migrateOnUserIdChange: true)))
            throw new HubException("Unauthorized");
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return;
        await Groups.AddToGroupAsync(Context.ConnectionId, OfferGroup(oid));
    }

    public async Task LeaveOffer(string offerId)
    {
        if (string.IsNullOrEmpty(await GetConnectionUserIdForHubCallAsync(
                Context.ConnectionAborted, migrateOnUserIdChange: false)))
            return;
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, OfferGroup(oid));
    }

    /// <summary>Aviso explícito «salir»: notifica al resto (vía <see cref="IChatService.BroadcastParticipantLeftToOthersAsync"/>) y deja el grupo del hilo.</summary>
    public async Task NotifyOthersUserLeftChat(string threadId)
    {
        var userId = await GetConnectionUserIdForHubCallAsync(
            Context.ConnectionAborted, migrateOnUserIdChange: true);
        if (string.IsNullOrEmpty(userId))
            throw new HubException("Unauthorized");

        var tid = ChatThreadIds.NormalizePersistedId(threadId);
        if (tid.Length < 4)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        if (!await chat.BroadcastParticipantLeftToOthersAsync(userId, tid, Context.ConnectionAborted))
            return;

        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            ChatHubGroupNames.ForThread(tid));
    }

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

    /// <summary>
    /// Resuelve el usuario actual para un método del hub.
    /// - Si el request (Authorization o <c>access_token</c> del negotiate) envía un token, debe validarse;
    ///   si el token dejó de ser válido, <b>no</b> reutilizamos el id de <c>OnConnected</c> (evita tomar
    ///   hilo A con identidad B cacheada) y forzamos al cliente a reconectar.
    /// - Si no hay credencial en el request, usamos el id fijado en <c>OnConnectedAsync</c> (WebSocket).
    /// Si el token válido resuelve a un id distinto del cache, migra la conexión a <c>user:{id}</c>.
    /// </summary>
    private async Task<string?> GetConnectionUserIdForHubCallAsync(
        CancellationToken cancellationToken,
        bool migrateOnUserIdChange)
    {
        var http = Context.GetHttpContext();
        if (http is not null
            && TryGetBearerHeader(http, out var rawBearer)
            && !string.IsNullOrWhiteSpace(rawBearer))
        {
            if (!auth.TryGetUserByToken(rawBearer, out var user) || user is null
                || string.IsNullOrWhiteSpace(user.Id))
            {
                return null;
            }

            var id = user.Id.Trim();
            if (id.Length < 1)
                return null;

            if (migrateOnUserIdChange
                && Context.Items.TryGetValue(CtxUserId, out var prevObj)
                && prevObj is string prev
                && prev.Trim().Length > 0
                && !string.Equals(prev.Trim(), id, StringComparison.Ordinal))
            {
                await Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    UserGroup(prev.Trim()),
                    cancellationToken);
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    UserGroup(id),
                    cancellationToken);
            }

            Context.Items[CtxUserId] = id;
            return id;
        }

        if (Context.Items[CtxUserId] is string cached)
        {
            var c = cached.Trim();
            if (c.Length > 0)
                return c;
        }

        return null;
    }

    private bool TryReadBearerUserId(out string userId)
    {
        userId = "";
        var http = Context.GetHttpContext();
        if (http is null || !TryGetBearerHeader(http, out var bearerHeader))
            return false;

        if (!auth.TryGetUserByToken(bearerHeader, out var user) || user is null
            || string.IsNullOrWhiteSpace(user.Id))
            return false;

        userId = user.Id.Trim();
        return userId.Length > 0;
    }
}
