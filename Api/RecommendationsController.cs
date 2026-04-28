using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Feed de recomendaciones personalizado e interacciones para afinar el ranking.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Recommendations")]
public sealed class RecommendationsController(
    IRecommendationService recommendations,
    IAuthService auth,
    IGuestRecommendationService guestRecommendations,
    IGuestInteractionStore guestInteractions) : ControllerBase
{
    public sealed record TrackInteractionBody(string? OfferId, string? EventType);

    /// <summary>Lote de ofertas recomendadas para el usuario autenticado (<c>take</c> opcional).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(RecommendationBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RecommendationBatchResponse>> Get(
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var batch = await recommendations.GetBatchAsync(
            userId,
            take ?? RecommendationService.DefaultBatchSize,
            cancellationToken);
        return Ok(batch);
    }

    /// <summary>Registra una interacción (<c>click</c>, <c>inquiry</c>, <c>chat_start</c>) para el ranking.</summary>
    [HttpPost("interactions")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostInteraction(
        [FromBody] TrackInteractionBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
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

    public sealed record TrackGuestInteractionBody(string? GuestId, string? OfferId, string? EventType);

    /// <summary>Feed de recomendaciones para un invitado (sin sesión).</summary>
    [HttpGet("guest")]
    [ProducesResponseType(typeof(RecommendationBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecommendationBatchResponse>> GetGuest(
        [FromQuery] string? guestId,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length < 8)
            return BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });

        var batch = await guestRecommendations.GetBatchAsync(
            gid,
            take ?? RecommendationService.DefaultBatchSize,
            cancellationToken);
        return Ok(batch);
    }

    /// <summary>Registra interacción de invitado en memoria para el ranking de <c>guest</c>.</summary>
    [HttpPost("guest/interactions")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult PostGuestInteraction([FromBody] TrackGuestInteractionBody body)
    {
        var gid = (body.GuestId ?? "").Trim();
        if (gid.Length < 8)
            return BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });
        if (string.IsNullOrWhiteSpace(body.OfferId))
            return BadRequest(new { error = "invalid_offer_id", message = "Indicá la oferta." });
        if (!TryParseEventType(body.EventType, out var eventType))
            return BadRequest(new { error = "invalid_event_type", message = "Usá click, inquiry o chat_start." });

        guestInteractions.Record(gid, body.OfferId.Trim(), eventType);
        return NoContent();
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
