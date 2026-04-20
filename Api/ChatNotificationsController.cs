using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Notificaciones del chat (comentarios en ofertas, likes, etc.).</summary>
[ApiController]
[Route("api/v1/me")]
[Produces("application/json")]
[Tags("Notifications")]
public sealed class ChatNotificationsController(IAuthService auth, IChatService chat) : ControllerBase
{
    public sealed record MarkReadBody(string[]? Ids);

    /// <summary>Lista notificaciones no leídas o recientes del usuario.</summary>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatNotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChatNotificationDto>>> GetNotifications(CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var list = await chat.ListNotificationsAsync(userId, cancellationToken);
        return Ok(list);
    }

    /// <summary>Marca notificaciones como leídas; si <c>ids</c> es null, marca todas.</summary>
    [HttpPost("notifications/mark-read")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadBody? body, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        await chat.MarkNotificationsReadAsync(userId, body?.Ids, cancellationToken);
        return NoContent();
    }
}
