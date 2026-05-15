using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Interfaces;

namespace VibeTrade.Backend.Api;

/// <summary>Acuerdos comerciales (mercancías/servicios) dentro del contexto de un hilo de chat.</summary>
[ApiController]
[Route("api/v1/chat")]
[Produces("application/json")]
[Tags("Chat", "Agreements")]
public sealed class ChatAgreementsController(
    ICurrentUserAccessor currentUser,
    ITradeAgreementService tradeAgreements) : ControllerBase
{
    /// <summary>Acuerdos del hilo (mercancías/servicios en tablas relacionales).</summary>
    [HttpGet("threads/{threadId}/trade-agreements")]
    [ProducesResponseType(typeof(IReadOnlyList<TradeAgreementApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTradeAgreements(string threadId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var list = await tradeAgreements.ListForThreadAsync(userId, threadId, cancellationToken);
        return Ok(list);
    }

    /// <summary>Emite un acuerdo (solo vendedor).</summary>
    [HttpPost("threads/{threadId}/trade-agreements")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TradeAgreementApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostTradeAgreement(
        string threadId,
        [FromBody] TradeAgreementDraftRequest body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var (created, writeErr) = await tradeAgreements.CreateAsync(userId, threadId, body, cancellationToken);
        if (writeErr == TradeAgreementWriteErrors.DuplicateAgreementTitle)
            return Conflict(new
            {
                error = writeErr,
                message = "En este chat ya hay un acuerdo con ese nombre. Elige otro título.",
            });
        if (created is null)
            return NotFound(new { error = "not_found", message = "No se pudo crear el acuerdo." });
        return Ok(created);
    }

    /// <summary>Actualiza borrador pendiente o rechazado, o revisa uno aceptado (solo vendedor).</summary>
    [HttpPatch("threads/{threadId}/trade-agreements/{agreementId}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TradeAgreementApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PatchTradeAgreement(
        string threadId,
        string agreementId,
        [FromBody] TradeAgreementDraftRequest body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var (updated, writeErr) = await tradeAgreements.UpdateAsync(userId, threadId, agreementId, body, cancellationToken);
        if (writeErr == TradeAgreementWriteErrors.DuplicateAgreementTitle)
            return Conflict(new
            {
                error = writeErr,
                message = "En este chat ya hay otro acuerdo con ese nombre. Elige otro título.",
            });
        if (updated is null)
            return NotFound(new { error = "not_found", message = "No se pudo actualizar el acuerdo." });
        return Ok(updated);
    }

    public sealed record TradeAgreementRouteLinkBody(string? RouteSheetId);

    /// <summary>Vincula o desvincula una hoja de ruta del acuerdo (solo vendedor; persiste en BD).</summary>
    [HttpPatch("threads/{threadId}/trade-agreements/{agreementId}/route-link")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TradeAgreementApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PatchTradeAgreementRouteLink(
        string threadId,
        string agreementId,
        [FromBody] TradeAgreementRouteLinkBody? body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var outcome = await tradeAgreements.SetRouteSheetLinkAsync(
            userId,
            threadId,
            agreementId,
            body?.RouteSheetId,
            cancellationToken);
        if (outcome.Response is not null)
            return Ok(outcome.Response);
        var code = outcome.FailureStatusCode ?? StatusCodes.Status404NotFound;
        var msg = outcome.FailureMessage ?? "No se pudo actualizar el vínculo con la hoja de ruta.";
        if (code == StatusCodes.Status400BadRequest)
            return BadRequest(new { error = "no_merchandise", message = msg });
        return NotFound(new { error = "not_found", message = msg });
    }

    public sealed record TradeAgreementRespondBody(bool Accept);

    /// <summary>Acepta o rechaza el acuerdo (solo comprador).</summary>
    [HttpPost("threads/{threadId}/trade-agreements/{agreementId}/respond")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TradeAgreementApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostTradeAgreementRespond(
        string threadId,
        string agreementId,
        [FromBody] TradeAgreementRespondBody body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var updated = await tradeAgreements.RespondAsync(userId, threadId, agreementId, body.Accept, cancellationToken);
        if (updated is null)
            return NotFound(new { error = "not_found", message = "No se pudo registrar la respuesta." });
        return Ok(updated);
    }

    /// <summary>Elimina un acuerdo no aceptado (solo vendedor).</summary>
    [HttpDelete("threads/{threadId}/trade-agreements/{agreementId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTradeAgreement(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var ok = await tradeAgreements.DeleteAsync(userId, threadId, agreementId, cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "No se pudo eliminar el acuerdo." });
        return NoContent();
    }
}
