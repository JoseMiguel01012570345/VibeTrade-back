using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Hilos de chat por oferta, mensajes y estado de entrega (participantes autenticados).</summary>
[ApiController]
[Route("api/v1/chat")]
[Produces("application/json")]
[Tags("Chat")]
public sealed class ChatController(
    IAuthService auth,
    IChatService chat,
    ITradeAgreementService tradeAgreements,
    IRouteSheetChatService routeSheets,
    IRouteTramoSubscriptionService routeTramoSubscriptions)
    : ControllerBase
{
    public sealed record CreateThreadBody(string OfferId, bool? PurchaseIntent);

    /// <summary>Crea o reutiliza el hilo comprador–vendedor para una oferta.</summary>
    /// <param name="body"><c>offerId</c> y opcional <c>purchaseIntent</c> (por defecto true).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("threads")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChatThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostThread([FromBody] CreateThreadBody body, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var purchaseIntent = body.PurchaseIntent ?? true;
        var oid = body.OfferId ?? "";
        if (await chat.IsUserSellerForOfferAsync(userId, oid, cancellationToken))
        {
            return BadRequest(new
            {
                error = "cannot_message_self",
                message = "No podés chatear con vos mismo.",
            });
        }

        var dto = await chat.CreateOrGetThreadForBuyerAsync(userId, oid, purchaseIntent, cancellationToken);
        if (dto is null)
            return NotFound(new { error = "offer_not_found", message = "No se encontró la oferta o no podés abrir este chat." });

        return Ok(dto);
    }

    /// <summary>Lista resumida de hilos donde participa el usuario.</summary>
    [HttpGet("threads")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatThreadSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ChatThreadSummaryDto>>> GetThreads(CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var list = await chat.ListThreadsForUserAsync(userId, cancellationToken);
        return Ok(list);
    }

    /// <summary>Obtiene el hilo visible para el usuario y la oferta indicada.</summary>
    [HttpGet("threads/by-offer/{offerId}")]
    [ProducesResponseType(typeof(ChatThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetThreadByOffer(string offerId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var dto = await chat.GetThreadByOfferIfVisibleAsync(userId, offerId, cancellationToken);
        if (dto is null)
            return NotFound();
        return Ok(dto);
    }

    /// <summary>Borrado lógico del hilo (solo si el usuario puede verlo).</summary>
    [HttpDelete("threads/{threadId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteThread(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var ok = await chat.DeleteThreadAsync(userId, threadId, cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "Hilo no encontrado o sin permiso." });
        return NoContent();
    }

    /// <summary>Detalle del hilo (participantes, tienda, modo compra).</summary>
    [HttpGet("threads/{threadId}")]
    [ProducesResponseType(typeof(ChatThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetThread(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var dto = await chat.GetThreadIfVisibleAsync(userId, threadId, cancellationToken);
        if (dto is null)
            return NotFound();
        return Ok(dto);
    }

    /// <summary>Historial de mensajes del hilo visibles para el usuario.</summary>
    [HttpGet("threads/{threadId}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var list = await chat.ListMessagesAsync(userId, threadId, cancellationToken);
        if (list.Count == 0)
        {
            var th = await chat.GetThreadIfVisibleAsync(userId, threadId, cancellationToken);
            if (th is null)
                return NotFound();
        }
        return Ok(list);
    }

    /// <summary>Envía un mensaje (texto, imagen, etc.) según el shape JSON esperado por el servicio.</summary>
    /// <param name="threadId">Id del hilo.</param>
    /// <param name="payload">Objeto de mensaje (tipo, cuerpo, citas, etc.).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("threads/{threadId}/messages")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostMessage(
        string threadId,
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var msg = await chat.PostMessageAsync(userId, threadId, payload, cancellationToken);
        if (msg is null)
            return NotFound(new { error = "not_found", message = "Hilo no encontrado o mensaje inválido." });
        return Ok(msg);
    }

    public sealed record UpdateMessageStatusBody(string Status);

    /// <summary>Actualiza el estado de entrega/lectura de un mensaje (p. ej. <c>read</c>, <c>delivered</c>).</summary>
    [HttpPost("threads/{threadId}/messages/{messageId}/status")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostMessageStatus(
        string threadId,
        string messageId,
        [FromBody] UpdateMessageStatusBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (!Enum.TryParse<ChatMessageStatus>(body.Status, ignoreCase: true, out var st))
            return BadRequest(new { error = "invalid_status" });
        var msg = await chat.UpdateMessageStatusAsync(userId, threadId, messageId, st, cancellationToken);
        if (msg is null)
            return NotFound(new { error = "not_found", message = "Mensaje o hilo no encontrado." });
        return Ok(msg);
    }

    /// <summary>Hojas de ruta persistidas del hilo (mismo contrato que <see cref="RouteSheetPayload"/>).</summary>
    [HttpGet("threads/{threadId}/route-sheets")]
    [ProducesResponseType(typeof(IReadOnlyList<RouteSheetPayload>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRouteSheets(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var list = await routeSheets.ListForThreadAsync(userId, threadId, cancellationToken);
        if (list is null)
            return NotFound();
        return Ok(list);
    }

    /// <summary>
    /// Suscripciones a tramos registradas en servidor para hojas publicadas en este hilo (visor en chat).
    /// </summary>
    [HttpGet("threads/{threadId}/route-tramo-subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<RouteTramoSubscriptionItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRouteTramoSubscriptions(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var list = await routeTramoSubscriptions.ListPublishedForThreadAsync(userId, threadId, cancellationToken);
        if (list is null)
            return NotFound();
        return Ok(list);
    }

    public sealed record AcceptRouteTramoSubscriptionBody(string RouteSheetId, string CarrierUserId, string? StopId = null);

    /// <summary>
    /// Solo vendedor del hilo: confirma las suscripciones pendientes del transportista en la hoja publicada y notifica al carrier.
    /// </summary>
    [HttpPost("threads/{threadId}/route-tramo-subscriptions/accept")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostAcceptRouteTramoSubscription(
        string threadId,
        [FromBody] AcceptRouteTramoSubscriptionBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.RouteSheetId)
            || string.IsNullOrWhiteSpace(body.CarrierUserId))
            return BadRequest(new { error = "invalid_body" });

        try
        {
            var n = await routeTramoSubscriptions.AcceptCarrierPendingOnSheetAsync(
                userId,
                threadId,
                body.RouteSheetId.Trim(),
                body.CarrierUserId.Trim(),
                string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim(),
                cancellationToken);
            if (n is null)
                return NotFound(new { error = "not_found", message = "No hay suscripciones que confirmar." });
            return Ok(new { acceptedCount = n.Value });
        }
        catch (TramoSubscriptionAcceptConflictException ex)
        {
            return Conflict(new { error = "tramo_already_confirmed", message = ex.Message });
        }
    }

    public sealed record RejectRouteTramoSubscriptionBody(string RouteSheetId, string CarrierUserId, string? StopId = null);

    /// <summary>
    /// Solo vendedor del hilo: rechaza solicitudes pendientes del transportista y notifica con enlace a la oferta de ruta (<c>emo_*</c>).
    /// </summary>
    [HttpPost("threads/{threadId}/route-tramo-subscriptions/reject")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostRejectRouteTramoSubscription(
        string threadId,
        [FromBody] RejectRouteTramoSubscriptionBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.RouteSheetId)
            || string.IsNullOrWhiteSpace(body.CarrierUserId))
            return BadRequest(new { error = "invalid_body" });

        var n = await routeTramoSubscriptions.RejectCarrierPendingOnSheetAsync(
            userId,
            threadId,
            body.RouteSheetId.Trim(),
            body.CarrierUserId.Trim(),
            string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim(),
            cancellationToken);
        if (n is null)
            return NotFound(new { error = "not_found", message = "No hay solicitudes pendientes que rechazar." });
        return Ok(new { rejectedCount = n.Value });
    }

    /// <summary>
    /// Transportista (no comprador/vendedor del hilo): abandona la operación, des-suscribe tramos y limpia teléfonos en hoja.
    /// </summary>
    [HttpPost("threads/{threadId}/route-tramo-subscriptions/carrier-withdraw")]
    [ProducesResponseType(typeof(CarrierWithdrawFromThreadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostCarrierWithdrawFromRouteSubscriptions(
        string threadId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var result = await routeTramoSubscriptions.WithdrawCarrierFromThreadAsync(
            userId,
            threadId,
            cancellationToken);
        if (result is null)
            return NotFound(new { error = "not_found", message = "No hay suscripciones activas que retirar." });
        return Ok(result);
    }

    /// <summary>Crea o actualiza una hoja de ruta en el hilo.</summary>
    [HttpPut("threads/{threadId}/route-sheets/{routeSheetId}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutRouteSheet(
        string threadId,
        string routeSheetId,
        [FromBody] RouteSheetPayload payload,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var ok = await routeSheets.UpsertAsync(userId, threadId, routeSheetId, payload, cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "Hilo no encontrado o datos inválidos." });
        return NoContent();
    }

    /// <summary>Elimina una hoja de ruta persistida y retira la señal emergente asociada.</summary>
    [HttpDelete("threads/{threadId}/route-sheets/{routeSheetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRouteSheet(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var ok = await routeSheets.DeleteAsync(userId, threadId, routeSheetId, cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "Hoja no encontrada o sin permiso." });
        return NoContent();
    }

    /// <summary>Acuerdos del hilo (mercancías/servicios en tablas relacionales).</summary>
    [HttpGet("threads/{threadId}/trade-agreements")]
    [ProducesResponseType(typeof(IReadOnlyList<TradeAgreementApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTradeAgreements(string threadId, CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
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
    public async Task<IActionResult> PostTradeAgreement(
        string threadId,
        [FromBody] TradeAgreementDraftRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var created = await tradeAgreements.CreateAsync(userId, threadId, body, cancellationToken);
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
    public async Task<IActionResult> PatchTradeAgreement(
        string threadId,
        string agreementId,
        [FromBody] TradeAgreementDraftRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var updated = await tradeAgreements.UpdateAsync(userId, threadId, agreementId, body, cancellationToken);
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
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var updated = await tradeAgreements.SetRouteSheetLinkAsync(
            userId,
            threadId,
            agreementId,
            body?.RouteSheetId,
            cancellationToken);
        if (updated is null)
            return NotFound(new { error = "not_found", message = "No se pudo actualizar el vínculo con la hoja de ruta." });
        return Ok(updated);
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
        var userId = BearerUserId.FromRequest(auth, Request);
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
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var ok = await tradeAgreements.DeleteAsync(userId, threadId, agreementId, cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "No se pudo eliminar el acuerdo." });
        return NoContent();
    }
}
