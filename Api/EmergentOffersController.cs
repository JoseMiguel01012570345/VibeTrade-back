using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.EmergentOffers;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Publicaciones emergentes (<c>emo_*</c>): reglas para transportistas.</summary>
[ApiController]
[Route("api/v1/emergent-offers")]
[Produces("application/json")]
[Tags("Emergent offers")]
public sealed class EmergentOffersController(
    IAuthService auth,
    IEmergentOfferCarrierSubscriptionService carrierSubscription,
    IEmergentRouteTramoSubscriptionRequestService tramoSubscriptionRequest) : ControllerBase
{
    public sealed record CarrierSubscriptionResponse(
        bool CanSubscribe,
        string? ReasonCode,
        string? Message);

    /// <summary>
    /// Indica si el usuario autenticado (o anónimo) puede suscribirse como transportista a esta publicación.
    /// Bloquea cuando el hilo vinculado tiene al usuario como comprador y existe un acuerdo aceptado.
    /// </summary>
    [HttpGet("{emergentOfferId}/carrier-subscription")]
    [ProducesResponseType(typeof(CarrierSubscriptionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CarrierSubscriptionResponse>> GetCarrierSubscription(
        string emergentOfferId,
        CancellationToken cancellationToken)
    {
        var viewerUserId = BearerUserId.FromRequest(auth, Request);
        var status = await carrierSubscription.GetStatusAsync(viewerUserId, emergentOfferId, cancellationToken);
        return Ok(new CarrierSubscriptionResponse(
            status.CanSubscribe,
            status.ReasonCode,
            status.Message));
    }

    public sealed record TramoSubscriptionRequestBody(string StopId, string StoreServiceId);

    /// <summary>
    /// Transportista autenticado: valida el servicio de catálogo y notifica a comprador y vendedor del hilo.
    /// </summary>
    [HttpPost("{emergentOfferId}/tramo-subscription-requests")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostTramoSubscriptionRequest(
        string emergentOfferId,
        [FromBody] TramoSubscriptionRequestBody? body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        if (body is null || string.IsNullOrWhiteSpace(body.StopId) || string.IsNullOrWhiteSpace(body.StoreServiceId))
            return BadRequest(new { error = "invalid_payload", message = "Indicá stopId y storeServiceId." });

        var (ok, code, message) = await tramoSubscriptionRequest.RequestAsync(
            userId,
            emergentOfferId,
            body.StopId.Trim(),
            body.StoreServiceId.Trim(),
            cancellationToken);

        if (!ok)
            return BadRequest(new { error = code, message });

        return NoContent();
    }
}
