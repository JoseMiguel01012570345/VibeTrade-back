using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Api;

[ApiController]
[Route("api/v1/chat/threads/{threadId}/agreements/{agreementId}/service-payments")]
[Produces("application/json")]
[Tags("Chat")]
public sealed class ChatAgreementServiceEvidenceController(
    ICurrentUserAccessor currentUser,
    IAgreementServiceEvidenceService svc) : ControllerBase
{
    private string? BearerId() => currentUser.GetUserId(Request);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementServicePaymentWithEvidenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListServicePayments(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = BearerId();
        if (userId is null) return Unauthorized();
        var (code, data) = await svc.ListAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);
        return code == StatusCodes.Status200OK ? Ok(data) : StatusCode(code);
    }

    [HttpPut("{paymentId}/evidence")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ServiceEvidenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertEvidence(
        string threadId,
        string agreementId,
        string paymentId,
        [FromBody] UpsertServiceEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerId();
        if (userId is null) return Unauthorized();
        var (code, err, data) = await svc.UpsertAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Ok(data);
        return code == StatusCodes.Status400BadRequest ? BadRequest(err) : StatusCode(code);
    }

    [HttpPost("{paymentId}/evidence/decision")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DecideEvidence(
        string threadId,
        string agreementId,
        string paymentId,
        [FromBody] DecideServiceEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerId();
        if (userId is null) return Unauthorized();
        var (code, err) = await svc.DecideAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Ok(new { ok = true });
        return code == StatusCodes.Status400BadRequest ? BadRequest(err) : StatusCode(code);
    }
    
    [HttpPost("{paymentId}/seller-payout")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordSellerPayout(
        string threadId,
        string agreementId,
        string paymentId,
        [FromBody] RecordSellerServicePayoutRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerId();
        if (userId is null) return Unauthorized();
        var (code, err) = await svc.RecordSellerPayoutAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Ok(new { ok = true });
        return code == StatusCodes.Status400BadRequest ? BadRequest(err) : StatusCode(code);
    }
}

