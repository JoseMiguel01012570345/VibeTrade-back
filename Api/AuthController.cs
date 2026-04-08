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

    public sealed record PatchProfileBody(
        string? Name,
        string? Email,
        string? Instagram,
        string? Telegram,
        string? XAccount,
        string? AvatarUrl);

    /// <summary>Actualiza campos del perfil persistidos (avatar referenciando <c>/api/v1/media/…</c>).</summary>
    [HttpPatch("profile")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> PatchProfile(
        [FromBody] PatchProfileBody body,
        CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();
        if (body is null)
            return BadRequest();

        var hasAny =
            body.Name is not null
            || body.Email is not null
            || body.Instagram is not null
            || body.Telegram is not null
            || body.XAccount is not null
            || body.AvatarUrl is not null;
        if (!hasAny)
            return BadRequest("Indicá al menos un campo.");

        if (!string.IsNullOrWhiteSpace(body.AvatarUrl))
        {
            var url = body.AvatarUrl.Trim();
            if (!url.StartsWith("/api/v1/media/", StringComparison.Ordinal))
                return BadRequest("AvatarUrl debe ser /api/v1/media/{id}.");
        }

        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return BadRequest("Sesión sin id de usuario.");
        var userId = idEl.GetString()!;

        await userAccountSync.PatchProfileAsync(
            userId,
            body.Name?.Trim(),
            body.Email?.Trim(),
            body.Instagram?.Trim(),
            body.Telegram?.Trim(),
            body.XAccount?.Trim(),
            body.AvatarUrl?.Trim(),
            cancellationToken);

        var snapshot = await userAccountSync.GetProfileSnapshotAsync(userId, cancellationToken);
        if (snapshot is null)
            return BadRequest("No se pudo leer el perfil persistido.");

        if (!auth.TrySyncSessionFromSnapshot(Request.Headers.Authorization, snapshot, out var updated))
            return Unauthorized();

        return Ok(new SessionResponse(updated));
    }

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

        // If the dev auth generated a new id, but we already have a persisted account for this phone,
        // reuse the persisted id so ownerUserId and bootstrap filtering remain stable across restarts.
        if (result.User.TryGetProperty("phone", out var ph) && ph.ValueKind == JsonValueKind.String)
        {
            var digits = new string(ph.GetString()!.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
            {
                var existingId = await userAccountSync.GetUserIdByPhoneDigitsAsync(digits, cancellationToken);
                if (!string.IsNullOrEmpty(existingId)
                    && result.User.TryGetProperty("id", out var curIdEl)
                    && curIdEl.ValueKind == JsonValueKind.String
                    && curIdEl.GetString() != existingId
                    && auth.TrySetSessionUserId("Bearer " + result.SessionToken, existingId, out var patched))
                {
                    // continue with patched user below
                }
            }
        }

        // La sesión nueva nace solo con el usuario ad-hoc; fusionar BD (avatar, nombre, email, redes)
        // para que tras cerrar sesión y volver a entrar el cliente reciba el mismo perfil que GET session.
        JsonElement userOut = result.User;
        if (auth.TryGetUserByToken("Bearer " + result.SessionToken, out var userNow))
            userOut = userNow;
        if (result.User.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var id = idEl.GetString()!;
            var snapshot = await userAccountSync.GetProfileSnapshotAsync(id, cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot("Bearer " + result.SessionToken, snapshot, out var merged))
                userOut = merged;
        }

        return Ok(new VerifyResponse(result.SessionToken, userOut));
    }
    
    /// <summary>Devuelve el perfil asociado al token actual.</summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> GetSession(CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();

        if (user.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var id = idEl.GetString()!;
            var snapshot = await userAccountSync.GetProfileSnapshotAsync(id, cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot(Request.Headers.Authorization, snapshot, out var merged))
                user = merged;
        }

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
