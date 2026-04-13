using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;

namespace VibeTrade.Backend.Api;

/// <summary>Carga inicial del cliente web: mercado persistido, reels vacíos y nombres de perfil vacíos.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class BootstrapController(IBootstrapService bootstrap, IAuthService auth) : ControllerBase
{
    /// <summary>Devuelve market, reels y profileDisplayNames.</summary>
    /// <remarks>Incluye cabecera recomendada <c>X-Timezone</c> (IANA) en todas las peticiones del cliente.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        string? viewerPhoneDigits = null;
        if (auth.TryGetUserByToken(Request.Headers.Authorization, out var user)
            && user.TryGetProperty("phone", out var ph)
            && ph.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            viewerPhoneDigits = new string(ph.GetString()!.Where(char.IsDigit).ToArray());
        }
        if (string.IsNullOrWhiteSpace(viewerPhoneDigits))
            return Unauthorized();

        using var doc = await bootstrap.GetBootstrapAsync(viewerPhoneDigits, cancellationToken);
        var json = doc.RootElement.GetRawText();
        return Content(json, "application/json");
    }
}

[ApiController]
[Route("api/v1/bootstrap/guest")]
[Produces("application/json")]
public sealed class GuestBootstrapController(IGuestBootstrapService bootstrap) : ControllerBase
{
    /// <summary>Bootstrap público para invitado (sin sesión).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] string? guestId, CancellationToken cancellationToken)
    {
        var gid = (guestId ?? "").Trim();
        if (gid.Length < 8)
            return BadRequest(new { error = "invalid_guest_id", message = "guestId requerido." });

        using var doc = await bootstrap.GetGuestBootstrapAsync(gid, cancellationToken);
        var json = doc.RootElement.GetRawText();
        return Content(json, "application/json");
    }
}
