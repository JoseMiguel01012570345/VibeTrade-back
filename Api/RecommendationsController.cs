using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Api;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class RecommendationsController(
    IRecommendationService recommendations,
    IAuthService auth) : ControllerBase
{
    public sealed record TrackInteractionBody(string? OfferId, string? EventType);

    [HttpGet]
    [ProducesResponseType(typeof(RecommendationBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RecommendationBatchResponse>> Get(
        [FromQuery] int? cursor,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized();

        var batch = await recommendations.GetBatchAsync(
            userId,
            take ?? RecommendationService.DefaultBatchSize,
            cursor ?? 0,
            cancellationToken);
        return Ok(batch);
    }

    [HttpPost("interactions")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostInteraction(
        [FromBody] TrackInteractionBody body,
        CancellationToken cancellationToken)
    {
        var userId = GetBearerUserId();
        if (userId is null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(body.OfferId))
            return BadRequest(new { error = "invalid_offer_id", message = "Indicá la oferta." });
        if (!TryParseEventType(body.EventType, out var eventType))
            return BadRequest(new { error = "invalid_event_type", message = "Usá click, inquiry o chat_start." });

        await recommendations.RecordInteractionAsync(
            userId,
            body.OfferId.Trim(),
            eventType,
            cancellationToken);
        return NoContent();
    }

    private string? GetBearerUserId()
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return null;
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        var id = idEl.GetString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static bool TryParseEventType(string? raw, out RecommendationInteractionType eventType)
    {
        switch ((raw ?? "").Trim().ToLowerInvariant())
        {
            case "click":
                eventType = RecommendationInteractionType.Click;
                return true;
            case "inquiry":
                eventType = RecommendationInteractionType.Inquiry;
                return true;
            case "chat_start":
                eventType = RecommendationInteractionType.ChatStart;
                return true;
            default:
                eventType = RecommendationInteractionType.Click;
                return false;
        }
    }
}
