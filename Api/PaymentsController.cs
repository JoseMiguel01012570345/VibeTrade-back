using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Payments.Interfaces;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Api;

/// <summary>Rutas de pago: Stripe bajo <c>/api/v1/payments/stripe/*</c> y checkout de acuerdos bajo <c>/api/v1/chat/threads/.../agreements/...</c> (URLs históricas).</summary>
[ApiController]
[Produces("application/json")]
[Tags("Payments")]
public sealed class PaymentsController(
    ICurrentUserAccessor currentUser,
    IPaymentsService payments,
    IRoutePathCheckoutQueryService routePathCheckout) : ControllerBase
{
    [HttpGet("/api/v1/payments/stripe/config")]
    [ProducesResponseType(typeof(StripeConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetStripeConfig()
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        return Ok(payments.GetStripeConfig());
    }

    [HttpGet("/api/v1/payments/stripe/payment-methods")]
    [ProducesResponseType(typeof(List<StripeCardPaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStripePaymentMethods(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var list = await payments.ListCardPaymentMethodsAsync(userId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(list);
    }

    [HttpPost("/api/v1/payments/stripe/setup-intents")]
    [ProducesResponseType(typeof(CreateSetupIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostStripeSetupIntent(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var (ok, problem, data) =
            await payments.CreateSetupIntentAsync(userId.Trim(), cancellationToken).ConfigureAwait(false);
        if (!ok || data is null)
            return BadRequest(problem);
        return Ok(data);
    }

    [HttpPost("/api/v1/payments/stripe/payment-intents")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreatePaymentIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostStripePaymentIntent(
        [FromBody] CreatePaymentIntentBody body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var (status, problem, data) =
            await payments.CreatePaymentIntentAsync(userId.Trim(), body, cancellationToken)
                .ConfigureAwait(false);
        if (status != StatusCodes.Status200OK || data is null)
            return StatusCode(status, problem);
        return Ok(data);
    }

    [HttpGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/checkout")]
    [ProducesResponseType(typeof(BreakdownDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgreementCheckout(
        string threadId,
        string agreementId,
        [FromQuery] string? routePathId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        IReadOnlyList<string>? routePaths = null;
        var pathFilter = (routePathId ?? "").Trim();
        if (pathFilter.Length > 0)
            routePaths = [pathFilter];

        var bd = await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null,
                routePaths, null, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return NotFound();
        return Ok(bd);
    }

    [HttpGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/payments")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementPaymentStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAgreementPayments(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        if (await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null, null, null, cancellationToken)
                .ConfigureAwait(false)
            is null)
            return NotFound();

        var list = await payments.ListPaymentStatusesAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(list);
    }

    public sealed record ServicePaymentPickBody(string ServiceItemId, int EntryMonth, int EntryDay);

    public sealed record ExecutePaymentBody(
        string Currency,
        string PaymentMethodId,
        string? IdempotencyKey,
        IReadOnlyList<ServicePaymentPickBody>? SelectedServicePayments,
        IReadOnlyList<string>? SelectedRoutePathIds,
        IReadOnlyList<string>? SelectedMerchandiseLineIds);

    public sealed record CheckoutBreakdownBody(
        IReadOnlyList<ServicePaymentPickBody>? SelectedServicePayments,
        IReadOnlyList<string>? SelectedRoutePathIds,
        IReadOnlyList<string>? SelectedMerchandiseLineIds);

    [HttpGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/route-paths")]
    [ProducesResponseType(typeof(AgreementRoutePathsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgreementRoutePaths(
        string threadId,
        string agreementId,
        [FromQuery] string routeSheetId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(routeSheetId))
            return BadRequest(new { error = "missing_route_sheet_id", message = "Indica routeSheetId." });

        var dto = await routePathCheckout
            .GetAgreementRoutePathsAsync(userId, threadId, agreementId, routeSheetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (dto is null)
            return NotFound();
        return Ok(dto);
    }

    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/checkout-breakdown")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BreakdownDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostAgreementCheckoutBreakdown(
        string threadId,
        string agreementId,
        [FromBody] CheckoutBreakdownBody body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var picks = body.SelectedServicePayments?
            .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
            .Select(x => new ServicePaymentPickDto(
                x.ServiceItemId.Trim(),
                x.EntryMonth,
                x.EntryDay))
            .ToList();

        var routePaths = body.SelectedRoutePathIds is null ? null : body.SelectedRoutePathIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var merchLines = body.SelectedMerchandiseLineIds is null ? null : body.SelectedMerchandiseLineIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var bd = await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, picks,
                routePaths, merchLines, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return NotFound();
        return Ok(bd);
    }

    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/payments/execute")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AgreementExecutePaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExecuteAgreementPayment(
        string threadId,
        string agreementId,
        [FromBody] ExecutePaymentBody body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var headerKey = (Request.Headers["Idempotency-Key"].FirstOrDefault() ?? "").Trim();
        var idem = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? headerKey : body.IdempotencyKey!.Trim();

        var routePathsExec = body.SelectedRoutePathIds is null ? null : body.SelectedRoutePathIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var merchExec = body.SelectedMerchandiseLineIds is null ? null : body.SelectedMerchandiseLineIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var r = await payments.ExecuteCurrencyPaymentAsync(
            userId,
            threadId,
            agreementId,
            body.Currency,
            body.PaymentMethodId,
            idem.Length >= 8 ? idem : null,
            body.SelectedServicePayments?
                .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
                .Select(x => new ServicePaymentPickDto(
                    x.ServiceItemId.Trim(),
                    x.EntryMonth,
                    x.EntryDay))
                .ToList(),
            routePathsExec,
            merchExec,
            cancellationToken).ConfigureAwait(false);

        if (r is null)
            return NotFound();

        if (!r.Accepted)
            return BadRequest(r);

        return Ok(r);
    }
}
