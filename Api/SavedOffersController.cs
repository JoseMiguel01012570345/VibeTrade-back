using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.SavedOffers;

namespace VibeTrade.Backend.Api;

/// <summary>Ofertas (producto/servicio) guardadas en el perfil del usuario autenticado.</summary>
[ApiController]
[Route("api/v1/me/saved-offers")]
[Produces("application/json")]
[Tags("Saved offers")]
public sealed class SavedOffersController(IAuthService auth, ISavedOffersService savedOffers) : ControllerBase
{
    public sealed record SaveBody(string ProductId);

    public sealed record SavedOfferIdsResponse(IReadOnlyList<string> SavedOfferIds);

    /// <summary>Guarda el id de producto/servicio. Rechaza si la oferta es de una tienda propia.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SavedOfferIdsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Post([FromBody] SaveBody body, CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user) || string.IsNullOrWhiteSpace(user?.Id))
            return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.ProductId))
            return BadRequest(new { error = "invalid_body", message = "Indica productId." });
        var userId = user.Id!;

        var (err, ids) = await savedOffers.TryAddAsync(userId, body.ProductId, cancellationToken);
        if (err == SavedOfferMutationError.UserNotFound)
            return NotFound(new { error = "user_not_found", message = "No se encontró la cuenta de usuario." });
        if (err == SavedOfferMutationError.NotFound)
            return NotFound(new { error = "not_found", message = "No existe una oferta con ese id." });
        if (err == SavedOfferMutationError.OwnProduct)
            return BadRequest(new
            {
                error = "own_product",
                message = "No puedes guardar productos o servicios de tus propias tiendas.",
            });

        return Ok(new SavedOfferIdsResponse(ids));
    }

    /// <summary>Quita un id de la lista guardada; devuelve la lista actualizada.</summary>
    [HttpDelete("{productId}")]
    [ProducesResponseType(typeof(SavedOfferIdsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(string productId, CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user) || string.IsNullOrWhiteSpace(user?.Id))
            return Unauthorized();
        var userId = user.Id!;

        var ids = await savedOffers.TryRemoveAsync(userId, productId, cancellationToken);
        if (ids is null)
            return Ok(new SavedOfferIdsResponse(Array.Empty<string>()));

        return Ok(new SavedOfferIdsResponse(ids));
    }
}
