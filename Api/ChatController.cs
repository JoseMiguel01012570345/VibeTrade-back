using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

/// <summary>Hilos de chat por oferta, mensajes y estado de entrega (participantes autenticados).</summary>
[ApiController]
[Route("api/v1/chat")]
[Produces("application/json")]
[Tags("Chat")]
public sealed class ChatController(IAuthService auth, IChatService chat) : ControllerBase
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
}
