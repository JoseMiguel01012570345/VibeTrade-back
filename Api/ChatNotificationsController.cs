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

    /// <summary>Lista notificaciones. Opcional: <c>from</c> y <c>to</c> (ISO 8601) filtran por <c>CreatedAtUtc</c>.</summary>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatNotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChatNotificationDto>>> GetNotifications(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateTimeOffset.TryParse(from!.Trim(), out var f))
                return BadRequest(new { error = "invalid_from", message = "Parámetro 'from' no es una fecha ISO válida." });
            fromUtc = f;
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateTimeOffset.TryParse(to!.Trim(), out var t))
                return BadRequest(new { error = "invalid_to", message = "Parámetro 'to' no es una fecha ISO válida." });
            toUtc = t;
        }
        if (fromUtc != null && toUtc != null && fromUtc > toUtc)
            return BadRequest(new { error = "invalid_range", message = "La fecha de inicio debe ser anterior o igual al fin." });
        var list = await chat.ListNotificationsAsync(userId, fromUtc, toUtc, cancellationToken);
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
