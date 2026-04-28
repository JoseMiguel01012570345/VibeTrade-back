using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;

namespace VibeTrade.Backend.Api;

/// <summary>Carga inicial del cliente web: mercado, recomendaciones, ofertas guardadas y (según sesión) hilos.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Bootstrap")]
public sealed class BootstrapController(IBootstrapService bootstrap, IAuthService auth) : ControllerBase
{
    /// <summary>Bootstrap autenticado: mercado + reels + recomendaciones + hilos del vendedor fusionados con PostgreSQL.</summary>
    /// <remarks>Requiere <c>Authorization: Bearer</c>.</remarks>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        string? viewerPhoneDigits = null;
        if (auth.TryGetUserByToken(Request.Headers.Authorization, out var user) && !string.IsNullOrEmpty(user?.Phone))
        {
            viewerPhoneDigits = new string(user.Phone.Where(char.IsDigit).ToArray());
        }
        if (string.IsNullOrWhiteSpace(viewerPhoneDigits))
            return Unauthorized();

        var root = await bootstrap.GetBootstrapAsync(viewerPhoneDigits, cancellationToken);
        return Ok(root);
    }
}

[ApiController]
[Route("api/v1/bootstrap/guest")]
[Produces("application/json")]
[Tags("Bootstrap")]
public sealed class GuestBootstrapController(IGuestBootstrapService bootstrap) : ControllerBase
{
    /// <summary>Bootstrap sin cuenta: mercado global, recomendaciones para <paramref name="guestId"/> y sin hilos.</summary>
    /// <param name="guestId">Identificador estable del invitado (mín. 8 caracteres).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? guestId, CancellationToken cancellationToken)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length < 8)
            return BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });

        var root = await bootstrap.GetGuestBootstrapAsync(gid, cancellationToken);
        return Ok(root);
    }
}
