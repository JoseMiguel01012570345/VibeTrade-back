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
    IEmergentOfferCarrierSubscriptionService carrierSubscription) : ControllerBase
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
}
