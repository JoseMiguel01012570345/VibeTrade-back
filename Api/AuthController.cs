using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Api;

/// <summary>OTP por teléfono, sesión Bearer, perfil persistido y agenda de contactos.</summary>
/// <remarks>
/// Los códigos OTP son aleatorios por solicitud. En desarrollo, <c>Auth:ExposeDevCodes</c> puede devolver <c>devMockCode</c> en la respuesta de <c>request-code</c>.
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Auth")]
public sealed class AuthController(
    IAuthService auth,
    IUserAccountSyncService userAccountSync,
    IUserContactsService contacts) : ControllerBase
{
    private static string? PhoneDigitsFromSessionUser(JsonElement user)
    {
        if (!user.TryGetProperty("phone", out var ph) || ph.ValueKind != JsonValueKind.String)
            return null;
        return new string(ph.GetString()!.Where(char.IsDigit).ToArray());
    }

    public sealed record RequestCodeBody(string Phone, string? Mode = null);

    public sealed record RequestCodeResponse(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

    public sealed record VerifyBody(string Phone, string Code, string? Mode = null);

    public sealed record VerifyResponse(string SessionToken, JsonElement User);

    public sealed record SessionResponse(JsonElement User);

    public sealed record PatchProfileBody(
        string? Name,
        string? Email,
        string? Instagram,
        string? Telegram,
        string? XAccount,
        string? AvatarUrl);

    /// <summary>Datos públicos para mostrar perfil de otro usuario (sin email/teléfono).</summary>
    public sealed record PublicUserProfileResponse(
        string Id,
        string Name,
        string? AvatarUrl,
        int TrustScore);

    public sealed record AddContactBody(string? Phone);

    /// <summary>Lista de contactos (cuentas de la plataforma) guardados por el usuario autenticado.</summary>
    /// <returns>Lista ordenada por fecha de alta del vínculo.</returns>
    [HttpGet("contacts")]
    [ProducesResponseType(typeof(IReadOnlyList<UserContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserContactDto>>> GetContacts(CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return Unauthorized();
        var userId = idEl.GetString()!;
        var list = await contacts.ListAsync(userId, cancellationToken);
        return Ok(list);
    }

    /// <summary>Añade un contacto por número de teléfono (debe existir una cuenta con ese número).</summary>
    /// <param name="body"><c>phone</c> en formato internacional o local según validación del servidor.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("contacts")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(UserContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserContactDto>> PostContact(
        [FromBody] AddContactBody body,
        CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return Unauthorized();
        var userId = idEl.GetString()!;
        try
        {
            var dto = await contacts.AddByPhoneAsync(userId, body.Phone ?? "", cancellationToken);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("ya está", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "contact_duplicate", message = msg });
            if (msg.Contains("no está registrado", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { error = "phone_not_registered", message = msg });
            return BadRequest(new { error = "invalid_contact", message = msg });
        }
    }

    /// <summary>Elimina un contacto de la lista por id de usuario de la plataforma.</summary>
    /// <param name="contactUserId">Id de la cuenta a quitar de la agenda.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpDelete("contacts/{contactUserId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteContact(string contactUserId, CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return Unauthorized();
        var userId = idEl.GetString()!;
        if (string.IsNullOrWhiteSpace(contactUserId))
            return NotFound();
        var ok = await contacts.RemoveAsync(userId, contactUserId.Trim(), cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Países con los que se puede iniciar sesión / registrarse (prefijos SMS). Público, sin token.</summary>
    [HttpGet("sign-in-countries")]
    [ProducesResponseType(typeof(IReadOnlyList<SignInCountryDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SignInCountryDto>> GetSignInCountries() =>
        Ok(SignInCountryCatalog.All);

    /// <summary>Perfil público de otro usuario (sin email ni teléfono).</summary>
    /// <param name="userId">Id de cuenta en la plataforma.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpGet("public-profile/{userId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicUserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicUserProfileResponse>> GetPublicProfile(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var id = (userId ?? "").Trim();
        if (id.Length < 2)
            return NotFound();
        var snap = await userAccountSync.GetProfileSnapshotByUserIdAsync(id, cancellationToken);
        if (snap is null)
            return NotFound();
        return Ok(new PublicUserProfileResponse(
            snap.Id,
            string.IsNullOrWhiteSpace(snap.DisplayName) ? "Usuario" : snap.DisplayName.Trim(),
            snap.AvatarUrl,
            snap.TrustScore));
    }

    /// <summary>Actualiza campos del perfil persistidos; <c>avatarUrl</c> debe ser ruta <c>/api/v1/media/{id}</c>.</summary>
    /// <param name="body">Al menos un campo distinto de null.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
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

        var phoneDigits = PhoneDigitsFromSessionUser(user);
        await userAccountSync.PatchProfileAsync(
            userId,
            body.Name?.Trim(),
            body.Email?.Trim(),
            body.Instagram?.Trim(),
            body.Telegram?.Trim(),
            body.XAccount?.Trim(),
            body.AvatarUrl?.Trim(),
            phoneDigits,
            cancellationToken);

        var snapshot = await userAccountSync.GetProfileSnapshotAsync(phoneDigits, cancellationToken);
        if (snapshot is null)
            return BadRequest("No se pudo leer el perfil persistido.");

        if (!auth.TrySyncSessionFromSnapshot(Request.Headers.Authorization, snapshot, out var updated))
            return Unauthorized();

        return Ok(new SessionResponse(updated));
    }

    /// <summary>Solicita un código OTP; longitud y caducidad vienen en la respuesta.</summary>
    /// <param name="body"><c>phone</c> y opcionalmente <c>mode=register</c> para altas.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("request-code")]
    [ProducesResponseType(typeof(RequestCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RequestCodeResponse>> RequestCode(
        [FromBody] RequestCodeBody body,
        CancellationToken cancellationToken)
    {
        if (IsRegisterMode(body.Mode)
            && await userAccountSync.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Iniciá sesión si es tu cuenta.",
                });
        }

        var r = auth.RequestCode(body.Phone);
        return Ok(new RequestCodeResponse(r.CodeLength, r.ExpiresInSeconds, r.DevMockCode));
    }

    /// <summary>Canjea el OTP por un token de sesión (<c>Authorization: Bearer</c> en siguientes llamadas).</summary>
    /// <param name="body"><c>phone</c>, <c>code</c> y opcionalmente <c>mode</c>.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(VerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyResponse>> Verify(
        [FromBody] VerifyBody body,
        CancellationToken cancellationToken)
    {
        if (IsRegisterMode(body.Mode)
            && await userAccountSync.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Iniciá sesión si es tu cuenta.",
                });
        }

        var result = await auth.Verify(body.Phone, body.Code, cancellationToken);
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
                var existingUser = await userAccountSync.GetProfileSnapshotAsync(digits, cancellationToken);
                if (existingUser is not null
                    && !string.IsNullOrEmpty(existingUser.Id)
                    && result.User.TryGetProperty("id", out var curIdEl)
                    && curIdEl.ValueKind == JsonValueKind.String
                    && curIdEl.GetString() != existingUser.Id
                    && auth.TrySetSessionUserId("Bearer " + result.SessionToken, existingUser.Id, out var patched))
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
            var snapshotDigits = PhoneDigitsFromSessionUser(result.User);
            var snapshot = await userAccountSync.GetProfileSnapshotAsync(snapshotDigits, cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot("Bearer " + result.SessionToken, snapshot, out var merged))
                userOut = merged;
        }

        return Ok(new VerifyResponse(result.SessionToken, userOut));
    }
    
    /// <summary>Devuelve el usuario JSON fusionado con el perfil persistido en base de datos.</summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> GetSession(CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out var user))
            return Unauthorized();

        if (user.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var snapshotDigits = PhoneDigitsFromSessionUser(user);
            var snapshot = await userAccountSync.GetProfileSnapshotAsync(snapshotDigits, cancellationToken);
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

    private static bool IsRegisterMode(string? mode) =>
        string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase);
}
