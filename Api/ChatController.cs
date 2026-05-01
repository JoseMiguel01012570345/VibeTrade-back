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
    public sealed record CreateThreadBody(string OfferId, bool? PurchaseIntent, bool? ForceNew);

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
        var forceNew = body.ForceNew ?? false;
        var oid = body.OfferId ?? "";
        if (await chat.IsUserSellerForOfferAsync(userId, oid, cancellationToken))
        {
            return BadRequest(new
            {
                error = "cannot_message_self",
                message = "No puedes chatear contigo mismo.",
            });
        }

        var dto = await chat.CreateOrGetThreadForBuyerAsync(userId, oid, purchaseIntent, forceNew, cancellationToken);
        if (dto is null)
            return NotFound(new { error = "offer_not_found", message = "No se encontró la oferta o no puedes abrir este chat." });

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

    /// <summary>
    /// Llamar tras iniciar sesión: reconoce en bloque <c>delivered</c> de mensajes entrantes pendientes
    /// para que el emisor y el hub reflejen la entrega.
    /// </summary>
    [HttpPost("ack-pending-delivery-on-login")]
    [ProducesResponseType(typeof(AckPendingDeliveryOnLoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AckPendingDeliveryOnLoginResult>> PostAckPendingDeliveryOnLogin(
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var n = await chat.AckAllPendingIncomingDeliveredAsync(userId, cancellationToken);
        return Ok(new AckPendingDeliveryOnLoginResult(n));
    }

    public sealed record AckPendingDeliveryOnLoginResult(int Applied);

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

    public sealed record PartySoftLeaveBody(string Reason);

    /// <summary>
    /// Comprador o vendedor con acuerdo aceptado: oculta el hilo en su lista, notifica al resto con el motivo; el hilo no se borra.
    /// </summary>
    public sealed record PartySoftLeaveOkBody(bool SkipClientTrustPenalty);

    [HttpPost("threads/{threadId}/party-soft-leave")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PartySoftLeaveOkBody), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> PostPartySoftLeave(
        string threadId,
        [FromBody] PartySoftLeaveBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var r = (body?.Reason ?? "").Trim();
        if (r.Length < 1)
            return BadRequest(new { error = "reason_required", message = "Indica un motivo para salir." });
        var result = await chat.SoftLeaveThreadAsPartyAsync(new PartySoftLeaveArgs(userId, threadId, r), cancellationToken);
        if (!result.Success)
        {
            if (string.Equals(result.ErrorCode, "held_payments_buyer", StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    error = result.ErrorCode,
                    message =
                        "No podés salir del chat mientras haya pagos de servicios retenidos (en espera). Esperá la liberación o el reembolso.",
                });
            }

            if (string.Equals(result.ErrorCode, "held_payments_seller_merchandise", StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    error = result.ErrorCode,
                    message =
                        "No podés salir del chat con pagos retenidos si el acuerdo incluye mercancía u otros rubros distintos de solo servicios.",
                });
            }

            if (string.Equals(result.ErrorCode, "service_evidence_pending", StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    error = result.ErrorCode,
                    message =
                        "No podés salir del chat mientras haya evidencia de servicio enviada al comprador sin respuesta (pendiente de aceptación o rechazo).",
                });
            }

            if (string.Equals(result.ErrorCode, "stripe_refund_failed", StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status502BadGateway,
                    new
                    {
                        error = result.ErrorCode,
                        message =
                            "No se pudieron reembolsar los pagos retenidos en este momento. Reintentá en unos minutos o contactá soporte.",
                    });
            }

            return NotFound(new { error = "not_found", message = "No se pudo registrar la salida (sin acuerdo aceptado o sin permiso)." });
        }

        return Ok(new { skipClientTrustPenalty = result.SkipClientTrustPenalty });
    }

    /// <summary>
    /// Aviso a la contraparte (y demás participantes) de que el usuario dejó de seguir el chat; emite
    /// <c>participantLeft</c> en SignalR al resto de participantes conectados. Sin acuerdo aceptado,
    /// se usa al «Salir» de la lista; con acuerdo, el flujo es <c>party-soft-leave</c> y no hace falta.
    /// </summary>
    [HttpPost("threads/{threadId}/notify-participant-left")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostNotifyParticipantLeft(
        string threadId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var ok = await chat.BroadcastParticipantLeftToOthersAsync(userId, threadId, cancellationToken);
        if (!ok)
            return NotFound();
        return NoContent();
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
    /// <param name="body">Mensaje (tipos: text, audio, image, doc, docs y opcional replyToIds).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("threads/{threadId}/messages")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostMessage(
        string threadId,
        [FromBody] PostChatMessageBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var msg = await chat.PostMessageAsync(new PostChatMessageArgs(userId, threadId, body), cancellationToken);
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
        var msg = await chat.UpdateMessageStatusAsync(
            new UpdateChatMessageStatusArgs(userId, threadId, messageId, st),
            cancellationToken);
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
    /// Transportista invitado (teléfono en tramo): datos de la hoja para modal presel sin acceso al hilo de chat.
    /// </summary>
    [HttpGet("threads/{threadId}/route-sheets/{routeSheetId}/presel-preview")]
    [ProducesResponseType(typeof(RouteSheetPayload), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRouteSheetPreselPreview(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        var payload = await routeSheets.GetPreselPreviewForCarrierAsync(
            userId,
            threadId,
            routeSheetId,
            cancellationToken);
        if (payload is null)
            return NotFound();
        return Ok(payload);
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
                new TramoSellerSheetAction(
                    userId,
                    threadId,
                    body.RouteSheetId.Trim(),
                    body.CarrierUserId.Trim(),
                    string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim()),
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
            new TramoSellerSheetAction(
                userId,
                threadId,
                body.RouteSheetId.Trim(),
                body.CarrierUserId.Trim(),
                string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim()),
            cancellationToken);
        if (n is null)
            return NotFound(new { error = "not_found", message = "No hay solicitudes pendientes que rechazar." });
        return Ok(new { rejectedCount = n.Value });
    }

    public sealed record SellerExpelCarrierBody(
        string CarrierUserId,
        string Reason,
        string? RouteSheetId = null,
        string? StopId = null);

    /// <summary>
    /// Solo vendedor del hilo: retira a un transportista (un tramo si van <c>routeSheetId</c> y <c>stopId</c>, o toda la operación),
    /// con motivo y notificación in-app.
    /// </summary>
    [HttpPost("threads/{threadId}/route-tramo-subscriptions/seller-expel-carrier")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CarrierExpelledBySellerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostSellerExpelCarrier(
        string threadId,
        [FromBody] SellerExpelCarrierBody? body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.CarrierUserId)
            || string.IsNullOrWhiteSpace((body.Reason ?? "").Trim()))
            return BadRequest(new { error = "invalid_body", message = "Indica al transportista y un motivo." });

        var rs = (body.RouteSheetId ?? "").Trim();
        var st = (body.StopId ?? "").Trim();
        if (rs.Length > 0 != st.Length > 0)
            return BadRequest(new
            {
                error = "invalid_body",
                message = "Para expulsar solo un tramo envía routeSheetId y stopId; para toda la operación, ninguno de los dos.",
            });

        var r = await routeTramoSubscriptions.ExpelCarrierBySellerFromThreadAsync(
            userId,
            threadId,
            body.CarrierUserId.Trim(),
            (body.Reason ?? "").Trim(),
            rs.Length > 0 ? rs : null,
            st.Length > 0 ? st : null,
            cancellationToken);
        if (r is null)
            return NotFound(new { error = "not_found", message = "No se pudo retirar al transportista (sin permiso o sin suscripciones activas)." });
        return Ok(r);
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
        if (RouteSheetPayloadValidator.Validate(payload) is { } validationMessage)
            return BadRequest(new { error = "validation", message = validationMessage });
        var result = await routeSheets.UpsertAsync(userId, threadId, routeSheetId, payload, cancellationToken);
        return result switch
        {
            RouteSheetMutationResult.Ok => NoContent(),
            RouteSheetMutationResult.LockedByPaidAgreement => Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar, eliminar ni publicar.",
            }),
            _ => NotFound(new { error = "not_found", message = "Hilo no encontrado o datos inválidos." }),
        };
    }

    public sealed record NotifyPreselectedBody(RouteSheetPreselectedInvite[]? Invites);

    /// <summary>
    /// Tras guardar la hoja: notifica solo por tramos incluidos en <c>invites</c> (teléfono nuevo o modificado en ese tramo).
    /// </summary>
    [HttpPost("threads/{threadId}/route-sheets/{routeSheetId}/notify-preselected")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(NotifyPreselectedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostNotifyRouteSheetPreselected(
        string threadId,
        string routeSheetId,
        [FromBody] NotifyPreselectedBody? body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body?.Invites is null || body.Invites.Length == 0)
            return BadRequest(new { error = "invalid_body", message = "Indica al menos un tramo con teléfono a notificar." });
        if (await routeSheets.RouteSheetIsLockedByPaidAgreementAsync(threadId, routeSheetId, cancellationToken))
            return Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar, eliminar ni publicar.",
            });
        var n = await routeSheets.NotifyPreselectedTransportistasAsync(
            userId,
            threadId,
            routeSheetId,
            body.Invites,
            cancellationToken);
        if (n < 0)
            return NotFound(new { error = "not_found", message = "Hilo o hoja no encontrados, o sin permiso." });
        return Ok(new NotifyPreselectedResult(n));
    }

    public sealed record NotifyPreselectedResult(int NotifiedCount);

    public sealed record CarrierPreselInviteBody(string RouteSheetId, string? StopId, bool Accepted);

    /// <summary>
    /// Transportista invitado vía teléfono en la hoja: <c>Accepted</c> true = suscripción y acceso al hilo;
    /// false = rechazo y notificación al vendedor.
    /// </summary>
    [HttpPost("threads/{threadId}/route-sheet-presel-invite")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostCarrierRespondPreselInvite(
        string threadId,
        [FromBody] CarrierPreselInviteBody? body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.RouteSheetId))
            return BadRequest(new { error = "invalid_body" });
        var stop = string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim();
        var rsid = body.RouteSheetId.Trim();
        var ok = await routeTramoSubscriptions.CarrierRespondPreselectedRouteInviteAsync(
            new CarrierPreselInviteRequest(userId, threadId, rsid, stop, body.Accepted),
            cancellationToken);
        var errMsg = body.Accepted
            ? "No se pudo confirmar (teléfono en hoja, hilo o permisos)."
            : "No se pudo registrar el rechazo.";
        if (!ok)
            return NotFound(new { error = "not_found", message = errMsg });
        return Ok(new { ok = true, accepted = body.Accepted });
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
        var result = await routeSheets.DeleteAsync(userId, threadId, routeSheetId, cancellationToken);
        return result switch
        {
            RouteSheetMutationResult.Ok => NoContent(),
            RouteSheetMutationResult.LockedByPaidAgreement => Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar, eliminar ni publicar.",
            }),
            _ => NotFound(new { error = "not_found", message = "Hoja no encontrada o sin permiso." }),
        };
    }

    public sealed record RouteSheetEditCarrierResponseBody(bool Accept);

    /// <summary>
    /// Transportista con tramo confirmado: acusa recepción de la última edición de la hoja (aceptar o rechazar).
    /// </summary>
    [HttpPost("threads/{threadId}/route-sheets/{routeSheetId}/edit-carrier-response")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostRouteSheetEditCarrierResponse(
        string threadId,
        string routeSheetId,
        [FromBody] RouteSheetEditCarrierResponseBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();
        if (body is null)
            return BadRequest(new { error = "invalid_body" });
        var ok = await routeSheets.CarrierRespondToSheetEditAsync(
            userId,
            threadId,
            routeSheetId,
            body.Accept,
            cancellationToken);
        if (!ok)
            return NotFound(new { error = "not_found", message = "Sin permiso, sin acuse pendiente o hoja inexistente." });
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostTradeAgreement(
        string threadId,
        [FromBody] TradeAgreementDraftRequest body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
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
        var userId = BearerUserId.FromRequest(auth, Request);
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
        var userId = BearerUserId.FromRequest(auth, Request);
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
