using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Policies.Dtos;
using VibeTrade.Backend.Features.Policies.Interfaces;

namespace VibeTrade.Backend.Api;

/// <summary>Políticas y operaciones de salida del chat (parte con acuerdo y transportista).</summary>
[ApiController]
[Route("api/v1/policies/chat")]
[Produces("application/json")]
[Tags("Policies")]
public sealed class PoliciesController(
    ICurrentUserAccessor currentUser,
    IChatExitPolicyRegistry chatExitPolicyRegistry,
    IChatExitOperationsService chatExitOperations,
    IRouteTramoSubscriptionService routeTramoSubscriptions) : ControllerBase
{
    /// <summary>
    /// Comprador o vendedor con acuerdo aceptado: oculta el hilo en su lista, notifica al resto con el motivo; el hilo no se borra.
    /// </summary>
    [HttpPost("threads/{threadId}/party-soft-leave")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PartySoftLeaveOkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> PostPartySoftLeave(
        string threadId,
        [FromBody] PartySoftLeaveBody body,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var r = (body?.Reason ?? "").Trim();
        if (r.Length < 1)
        {
            chatExitPolicyRegistry.TryMapPartySoftLeaveFailure("reason_required", out var st, out var msg);
            return StatusCode(st, new { error = "reason_required", message = msg });
        }

        var result = await chatExitOperations.PartySoftLeaveAsync(
                new PartySoftLeaveArgs(userId, threadId, r),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var (status, errBody) = chatExitPolicyRegistry.PartySoftLeaveFailure(result.ErrorCode);
            return StatusCode(status, errBody);
        }

        return Ok(
            new PartySoftLeaveOkResponse(
                result.SkipClientTrustPenalty,
                result.OtherMemberCount,
                result.OtherMemberPenaltyApplied,
                result.TrustScoreAfterMemberPenalty));
    }

    /// <summary>
    /// Transportista (no comprador/vendedor del hilo): abandona la operación, des-suscribe tramos y limpia teléfonos en hoja.
    /// </summary>
    [HttpPost("threads/{threadId}/route-tramo-subscriptions/carrier-withdraw")]
    [ProducesResponseType(typeof(CarrierWithdrawFromThreadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostCarrierWithdrawFromRouteSubscriptions(
        string threadId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();

        var result = await routeTramoSubscriptions.WithdrawCarrierFromThreadAsync(userId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
            return NotFound(new { error = "not_found", message = "No hay suscripciones activas que retirar." });

        if (chatExitPolicyRegistry.TryMapCarrierWithdrawFailure(result.ErrorCode, out var carrierStatus, out var carrierMessage))
        {
            return StatusCode(
                carrierStatus,
                new { error = result.ErrorCode, message = carrierMessage });
        }

        return Ok(result);
    }
}
