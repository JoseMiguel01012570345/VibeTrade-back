using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Infrastructure;

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
    ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Lista de contactos (cuentas de la plataforma) guardados por el usuario autenticado.</summary>
    /// <returns>Lista ordenada por fecha de alta del vínculo.</returns>
    [HttpGet("contacts")]
    [ProducesResponseType(typeof(IReadOnlyList<UserContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserContactDto>>> GetContacts(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var list = await auth.ListContactsAsync(userId, cancellationToken);
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
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        try
        {
            var dto = await auth.AddContactByPhoneAsync(userId, body.Phone ?? "", cancellationToken);
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

    /// <summary>Busca un usuario registrado por número (sin añadirlo a la agenda).</summary>
    [HttpGet("contacts/resolve")]
    [ProducesResponseType(typeof(PlatformUserByPhoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformUserByPhoneDto>> GetContactResolve(
        [FromQuery] string? phone,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        try
        {
            var dto = await auth.ResolveContactByPhoneAsync(userId, phone ?? "", cancellationToken);
            if (dto is null)
                return NotFound(new { error = "phone_not_registered", message = "Ese número no está registrado en la plataforma." });
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_phone", message = ex.Message });
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
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(contactUserId))
            return NotFound();
        var ok = await auth.RemoveContactAsync(userId, contactUserId.Trim(), cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Países con los que se puede iniciar sesión / registrarse (prefijos SMS). Público, sin token.</summary>
    [HttpGet("sign-in-countries")]
    [ProducesResponseType(typeof(IReadOnlyList<SignInCountryDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SignInCountryDto>> GetSignInCountries() =>
        Ok(AuthService.SignInCountries);

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
        var snap = await auth.GetProfileSnapshotByUserIdAsync(id, cancellationToken);
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
        if (!currentUser.TryGetUser(Request, out var user) || string.IsNullOrWhiteSpace(user?.Id))
            return Unauthorized();
        if (body is null)
            return BadRequest();

        var userId = user!.Id!;

        var hasAny =
            body.Name is not null
            || body.Username is not null
            || body.Email is not null
            || body.Instagram is not null
            || body.Telegram is not null
            || body.XAccount is not null
            || body.AvatarUrl is not null;
        if (!hasAny)
            return BadRequest("Indica al menos un campo.");

        if (body.Username is not null)
        {
            var username = body.Username.Trim();
            if (!AuthUtils.IsValidUsername(username))
            {
                return BadRequest(new { error = "invalid_username", message = "Usuario inválido (3–32 caracteres, letras, números o _)." });
            }

            var usernameErr = await auth.TrySetUsernameAsync(userId, username, cancellationToken);
            if (usernameErr == "username_already_set")
            {
                return BadRequest(new { error = "username_already_set", message = "El nombre de usuario ya está definido y no se puede cambiar." });
            }

            if (usernameErr == "username_taken")
            {
                return Conflict(new { error = "username_taken", message = "Ese nombre de usuario ya está en uso." });
            }

            if (usernameErr is not null)
                return BadRequest(new { error = usernameErr, message = "No se pudo guardar el usuario." });
        }

        if (!string.IsNullOrWhiteSpace(body.AvatarUrl))
        {
            var url = body.AvatarUrl.Trim();
            if (!url.StartsWith("/api/v1/media/", StringComparison.Ordinal))
                return BadRequest("AvatarUrl debe ser /api/v1/media/{id}.");
        }

        var phoneDigits = AuthUtils.PhoneDigitsFromSessionUser(user);
        await auth.PatchProfileAsync(
            userId,
            body.Name?.Trim(),
            body.Email?.Trim(),
            body.Instagram?.Trim(),
            body.Telegram?.Trim(),
            body.XAccount?.Trim(),
            body.AvatarUrl?.Trim(),
            phoneDigits,
            cancellationToken);

        var snapshot =
            await auth.GetProfileSnapshotByUserIdAsync(userId, cancellationToken)
            ?? await auth.GetProfileSnapshotAsync(phoneDigits, cancellationToken);
        if (snapshot is null)
            return BadRequest("No se pudo leer el perfil persistido.");

        if (!auth.TrySyncSessionFromSnapshot(Request.Headers.Authorization, snapshot, out var updated) || updated is null)
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
            && await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Inicia sesión si es tu cuenta.",
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
            && await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Inicia sesión si es tu cuenta.",
                });
        }

        var result = await auth.Verify(body.Phone, body.Code, cancellationToken);
        if (result is null)
            return Unauthorized();
        await auth.UpsertFromSessionUserAsync(result.User, cancellationToken);

        if (!string.IsNullOrEmpty(result.User.Phone))
        {
            var digits = new string(result.User.Phone.Where(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                var existingUser = await auth.GetProfileSnapshotAsync(digits, cancellationToken);
                if (existingUser is not null
                    && !string.IsNullOrEmpty(existingUser.Id)
                    && !string.IsNullOrEmpty(result.User.Id)
                    && result.User.Id != existingUser.Id
                    && auth.TrySetSessionUserId("Bearer " + result.SessionToken, existingUser.Id, out _))
                {
                    // continue with patched user below
                }
            }
        }

        SessionUser userOut = result.User;
        if (auth.TryGetUserByToken("Bearer " + result.SessionToken, out var userNow) && userNow is not null)
            userOut = userNow;
        if (!string.IsNullOrWhiteSpace(userOut.Id))
        {
            var id = userOut.Id;
            var snapshot =
                await auth.GetProfileSnapshotByUserIdAsync(id, cancellationToken)
                ?? await auth.GetProfileSnapshotAsync(AuthUtils.PhoneDigitsFromSessionUser(userOut), cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot("Bearer " + result.SessionToken, snapshot, out var merged) &&
                merged is not null)
                userOut = merged;
        }

        return Ok(new VerifyResponse(result.SessionToken, userOut));
    }

    /// <summary>Inicia sesión con email y contraseña.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(VerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyResponse>> Login(
        [FromBody] LoginBody body,
        CancellationToken cancellationToken)
    {
        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5 || string.IsNullOrWhiteSpace(body.Password))
            return Unauthorized(new { error = "invalid_credentials", message = "Email o contraseña incorrectos." });

        var result = await auth.LoginAsync(email, body.Password ?? "", cancellationToken);
        if (result is null)
        {
            return Unauthorized(new { error = "invalid_credentials", message = "Email o contraseña incorrectos." });
        }

        return Ok(new VerifyResponse(result.SessionToken, result.User));
    }

    /// <summary>Registro: contraseña, email y teléfono. Devuelve OTP de teléfono (devMockCode en desarrollo).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterStartResponse>> Register(
        [FromBody] RegisterBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8)
            return BadRequest(new { error = "invalid_password", message = "La contraseña debe tener al menos 8 caracteres." });

        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5 || !email.Contains('@'))
            return BadRequest(new { error = "invalid_email", message = "Email inválido." });

        if (await auth.EmailHasRegisteredAccountAsync(email, cancellationToken))
            return Conflict(new { error = "email_taken", message = "Ese email ya está registrado." });

        if (await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
            return Conflict(new { error = "phone_taken", message = "Ese teléfono ya está registrado." });

        var result = await auth.StartRegistrationAsync(
            body.Password!,
            email,
            body.Phone ?? "",
            cancellationToken);
        if (result is null)
            return BadRequest(new { error = "registration_failed", message = "No se pudo iniciar el registro." });

        return Ok(new RegisterStartResponse(
            result.RegistrationId,
            result.CodeLength,
            result.ExpiresInSeconds,
            result.DevMockCode));
    }

    /// <summary>Verifica teléfono en registro; devuelve devMockCode del email.</summary>
    [HttpPost("register/verify-phone")]
    [ProducesResponseType(typeof(VerifyPhoneResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyPhoneResponse>> RegisterVerifyPhone(
        [FromBody] VerifyRegistrationBody body,
        CancellationToken cancellationToken)
    {
        var result = await auth.VerifyRegistrationPhoneAsync(
            body.RegistrationId ?? "",
            body.Code ?? "",
            cancellationToken);
        if (result is null)
            return Unauthorized(new { error = "invalid_code", message = "Código inválido o registro expirado." });

        return Ok(new VerifyPhoneResponse(result.CodeLength, result.ExpiresInSeconds, result.DevMockCode));
    }

    /// <summary>Verifica email en registro y crea sesión.</summary>
    [HttpPost("register/verify-email")]
    [ProducesResponseType(typeof(VerifyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyResponse>> RegisterVerifyEmail(
        [FromBody] VerifyRegistrationBody body,
        CancellationToken cancellationToken)
    {
        var result = await auth.VerifyRegistrationEmailAsync(
            body.RegistrationId ?? "",
            body.Code ?? "",
            cancellationToken);
        if (result is null)
            return Unauthorized(new { error = "invalid_code", message = "Código inválido o registro expirado." });

        return Ok(new VerifyResponse(result.SessionToken, result.User));
    }

    /// <summary>Solicita confirmación de cambio de contraseña por email.</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        [FromBody] ForgotPasswordBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < 8)
            return BadRequest(new { error = "invalid_password", message = "La contraseña debe tener al menos 8 caracteres." });

        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5)
            return BadRequest(new { error = "invalid_email", message = "Email inválido." });

        if (!await auth.EmailHasRegisteredAccountAsync(email, cancellationToken))
            return NotFound(new { error = "email_not_found", message = "No hay cuenta con ese email." });

        var result = await auth.RequestPasswordResetAsync(email, body.NewPassword!, cancellationToken);
        if (result is null)
            return BadRequest(new { error = "reset_failed", message = "No se pudo solicitar el cambio." });

        return Ok(new ForgotPasswordResponse(result.CodeLength, result.ExpiresInSeconds, result.DevMockCode));
    }

    /// <summary>Confirma cambio de contraseña con código de email.</summary>
    [HttpPost("confirm-password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmPasswordReset(
        [FromBody] ConfirmPasswordResetBody body,
        CancellationToken cancellationToken)
    {
        var ok = await auth.ConfirmPasswordResetAsync(
            body.Email ?? "",
            body.Code ?? "",
            cancellationToken);
        if (!ok)
            return Unauthorized(new { error = "invalid_code", message = "Código inválido o expirado." });

        return NoContent();
    }

    /// <summary>Devuelve el usuario JSON fusionado con el perfil persistido en base de datos.</summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> GetSession(CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetUser(Request, out var user) || user is null)
            return Unauthorized();

        if (!string.IsNullOrWhiteSpace(user.Id))
        {
            var id = user.Id;
            var snapshot =
                await auth.GetProfileSnapshotByUserIdAsync(id, cancellationToken)
                ?? await auth.GetProfileSnapshotAsync(AuthUtils.PhoneDigitsFromSessionUser(user), cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot(Request.Headers.Authorization, snapshot, out var merged) &&
                merged is not null)
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
