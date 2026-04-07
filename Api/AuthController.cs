using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Api;

/// <summary>OTP y sesiones en memoria. Código aleatorio por solicitud; expuesto como <c>devMockCode</c> si <c>Auth:ExposeDevCodes</c> está activo.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth, IUserAccountSyncService userAccountSync) : ControllerBase
{
    public sealed record RequestCodeBody(string Phone);

    public sealed record RequestCodeResponse(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

    public sealed record VerifyBody(string Phone, string Code);

    public sealed record VerifyResponse(string SessionToken, JsonElement User);

    public sealed record SessionResponse(JsonElement User);

    /// <summary>Pide un código OTP (7 dígitos aleatorios por solicitud).</summary>
    [HttpPost("request-code")]
    [ProducesResponseType(typeof(RequestCodeResponse), StatusCodes.Status200OK)]
    public ActionResult<RequestCodeResponse> RequestCode([FromBody] RequestCodeBody body)
    {
        var r = auth.RequestCode(body.Phone);
        return Ok(new RequestCodeResponse(r.CodeLength, r.ExpiresInSeconds, r.DevMockCode));
    }

    /// <summary>Canjea el OTP por un token de sesión (<c>Bearer</c> en siguientes llamadas).</summary>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(VerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyResponse>> Verify(
        [FromBody] VerifyBody body,
        CancellationToken cancellationToken)
    {
        var result = auth.Verify(body.Phone, body.Code);
        if (result is null)
            return Unauthorized();
        await userAccountSync.UpsertFromSessionUserAsync(result.User, cancellationToken);
        return Ok(new VerifyResponse(result.SessionToken, result.User));
    }

    /// <summary>Devuelve el perfil asociado al token actual.</summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<SessionResponse> GetSession()
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();
        return Ok(new SessionResponse(user));
    }

    /// <summary>Cierra la sesión en el servidor (idempotente si el token ya era inválido).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        auth.RevokeSession(Request.Headers.Authorization);
        return NoContent();
    }
}
