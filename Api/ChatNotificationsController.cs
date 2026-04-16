using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Api;

[ApiController]
[Route("api/v1/me")]
[Produces("application/json")]
public sealed class ChatNotificationsController(IAuthService auth, IChatService chat) : ControllerBase
{
    public sealed record MarkReadBody(string[]? Ids);

    [HttpGet("notifications")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatNotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChatNotificationDto>>> GetNotifications(CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized();
        var list = await chat.ListNotificationsAsync(userId, cancellationToken);
        return Ok(list);
    }

    [HttpPost("notifications/mark-read")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadBody? body, CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized();
        await chat.MarkNotificationsReadAsync(userId, body?.Ids, cancellationToken);
        return NoContent();
    }

    private string? GetBearerUserId()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHdr))
            return null;
        if (!auth.TryGetUserByToken(authHdr, out var user))
            return null;
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return null;
        var id = idEl.GetString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
