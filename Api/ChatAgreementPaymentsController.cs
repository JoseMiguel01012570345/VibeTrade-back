using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

[ApiController]
[Route("api/v1/chat/threads/{threadId}/agreements/{agreementId}")]
[Produces("application/json")]
[Tags("Chat")]
public sealed class ChatAgreementPaymentsController(
    IAuthService auth,
    IAgreementCheckoutService checkout) : ControllerBase
{
    [HttpGet("checkout")]
    [ProducesResponseType(typeof(PaymentCheckoutComputation.BreakdownDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCheckout(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var bd = await checkout.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return NotFound();
        return Ok(bd);
    }

    [HttpGet("payments")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementPaymentStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPayments(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        if (await checkout.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null, cancellationToken)
                .ConfigureAwait(false)
            is null)
            return NotFound();

        var list = await checkout.ListPaymentStatusesAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(list);
    }

    public sealed record ServicePaymentPickBody(string ServiceItemId, int EntryMonth, int EntryDay);

    public sealed record ExecutePaymentBody(
        string Currency,
        string PaymentMethodId,
        string? IdempotencyKey,
        IReadOnlyList<ServicePaymentPickBody>? SelectedServicePayments);

    public sealed record CheckoutBreakdownBody(IReadOnlyList<ServicePaymentPickBody>? SelectedServicePayments);

    [HttpPost("checkout-breakdown")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentCheckoutComputation.BreakdownDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostCheckoutBreakdown(
        string threadId,
        string agreementId,
        [FromBody] CheckoutBreakdownBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var picks = body.SelectedServicePayments?
            .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
            .Select(x => new PaymentCheckoutComputation.ServicePaymentPickDto(
                x.ServiceItemId.Trim(),
                x.EntryMonth,
                x.EntryDay))
            .ToList();

        var bd = await checkout.GetCheckoutBreakdownAsync(userId, threadId, agreementId, picks, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return NotFound();
        return Ok(bd);
    }

    [HttpPost("payments/execute")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AgreementExecutePaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExecutePayment(
        string threadId,
        string agreementId,
        [FromBody] ExecutePaymentBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var headerKey = (Request.Headers["Idempotency-Key"].FirstOrDefault() ?? "").Trim();
        var idem = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? headerKey : body.IdempotencyKey!.Trim();

        var r = await checkout.ExecuteCurrencyPaymentAsync(
            userId,
            threadId,
            agreementId,
            body.Currency,
            body.PaymentMethodId,
            idem.Length >= 8 ? idem : null,
            body.SelectedServicePayments?
                .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
                .Select(x => new PaymentCheckoutComputation.ServicePaymentPickDto(
                    x.ServiceItemId.Trim(),
                    x.EntryMonth,
                    x.EntryDay))
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        if (r is null)
            return NotFound();

        if (!r.Accepted)
            return BadRequest(r);

        return Ok(r);
    }
}
