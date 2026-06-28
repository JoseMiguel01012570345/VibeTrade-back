using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapGet("/contacts", GetContactsAsync);
        group.MapPost("/contacts", PostContactAsync);
        group.MapGet("/contacts/resolve", GetContactResolveAsync);
        group.MapDelete("/contacts/{contactUserId}", DeleteContactAsync);
        group.MapGet("/sign-in-countries", GetSignInCountries);
        group.MapGet("/public-profile/{userId}", GetPublicProfileAsync).AllowAnonymous();
        group.MapPatch("/profile", PatchProfileAsync);
        group.MapPost("/request-code", RequestCodeAsync);
        group.MapPost("/verify", VerifyAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/register/verify-phone", RegisterVerifyPhoneAsync);
        group.MapPost("/register/verify-email", RegisterVerifyEmailAsync);
        group.MapPost("/forgot-password", ForgotPasswordAsync);
        group.MapPost("/confirm-password-reset", ConfirmPasswordResetAsync);
        group.MapGet("/session", GetSessionAsync);
        group.MapPost("/logout", Logout);

        return app;
    }

    private static async Task<IResult> GetContactsAsync(
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await auth.ListContactsAsync(userId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostContactAsync(
        AddContactBody body,
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        try
        {
            var dto = await auth.AddContactByPhoneAsync(userId, body.Phone ?? "", cancellationToken);
            return Results.Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("ya está", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { error = "contact_duplicate", message = msg });
            if (msg.Contains("no está registrado", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound(new { error = "phone_not_registered", message = msg });
            return Results.BadRequest(new { error = "invalid_contact", message = msg });
        }
    }

    private static async Task<IResult> GetContactResolveAsync(
        string? phone,
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        try
        {
            var dto = await auth.ResolveContactByPhoneAsync(userId, phone ?? "", cancellationToken);
            if (dto is null)
                return Results.NotFound(new { error = "phone_not_registered", message = "Ese número no está registrado en la plataforma." });
            return Results.Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = "invalid_phone", message = ex.Message });
        }
    }

    private static async Task<IResult> DeleteContactAsync(
        string contactUserId,
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(contactUserId))
            return Results.NotFound();
        var ok = await auth.RemoveContactAsync(userId, contactUserId.Trim(), cancellationToken);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static IResult GetSignInCountries() =>
        Results.Ok(AuthService.SignInCountries);

    private static async Task<IResult> GetPublicProfileAsync(
        string userId,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        var id = (userId ?? "").Trim();
        if (id.Length < 2)
            return Results.NotFound();
        var snap = await auth.GetProfileSnapshotByUserIdAsync(id, cancellationToken);
        if (snap is null)
            return Results.NotFound();
        return Results.Ok(new PublicUserProfileResponse(
            snap.Id,
            string.IsNullOrWhiteSpace(snap.DisplayName) ? "Usuario" : snap.DisplayName.Trim(),
            snap.AvatarUrl,
            snap.TrustScore));
    }

    private static async Task<IResult> PatchProfileAsync(
        PatchProfileBody body,
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetUser(request, out var user) || string.IsNullOrWhiteSpace(user?.Id))
            return Results.Unauthorized();
        if (body is null)
            return Results.BadRequest();

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
            return Results.BadRequest("Indica al menos un campo.");

        if (body.Username is not null)
        {
            var username = body.Username.Trim();
            if (!AuthUtils.IsValidUsername(username))
            {
                return Results.BadRequest(new { error = "invalid_username", message = "Usuario inválido (3–32 caracteres, letras, números o _)." });
            }

            var usernameErr = await auth.TrySetUsernameAsync(userId, username, cancellationToken);
            if (usernameErr == "username_already_set")
            {
                return Results.BadRequest(new { error = "username_already_set", message = "El nombre de usuario ya está definido y no se puede cambiar." });
            }

            if (usernameErr == "username_taken")
            {
                return Results.Conflict(new { error = "username_taken", message = "Ese nombre de usuario ya está en uso." });
            }

            if (usernameErr is not null)
                return Results.BadRequest(new { error = usernameErr, message = "No se pudo guardar el usuario." });
        }

        if (!string.IsNullOrWhiteSpace(body.AvatarUrl))
        {
            var url = body.AvatarUrl.Trim();
            if (!url.StartsWith("/api/v1/media/", StringComparison.Ordinal))
                return Results.BadRequest("AvatarUrl debe ser /api/v1/media/{id}.");
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
            return Results.BadRequest("No se pudo leer el perfil persistido.");

        if (!auth.TrySyncSessionFromSnapshot(request.Headers.Authorization, snapshot, out var updated) || updated is null)
            return Results.Unauthorized();

        return Results.Ok(new SessionResponse(updated));
    }

    private static async Task<IResult> RequestCodeAsync(
        RequestCodeBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (IsRegisterMode(body.Mode)
            && await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return Results.BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Inicia sesión si es tu cuenta.",
                });
        }

        var r = auth.RequestCode(body.Phone);
        return Results.Ok(new RequestCodeResponse(r.CodeLength, r.ExpiresInSeconds, r.DevMockCode));
    }

    private static async Task<IResult> VerifyAsync(
        VerifyBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (IsRegisterMode(body.Mode)
            && await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
        {
            return Results.BadRequest(
                new
                {
                    error = "phone_already_registered",
                    message = "Este número ya está registrado. Inicia sesión si es tu cuenta.",
                });
        }

        var result = await auth.Verify(body.Phone, body.Code, cancellationToken);
        if (result is null)
            return Results.Unauthorized();
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

        return Results.Ok(new VerifyResponse(result.SessionToken, userOut));
    }

    private static async Task<IResult> LoginAsync(
        LoginBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5 || string.IsNullOrWhiteSpace(body.Password))
            return Results.Json(new { error = "invalid_credentials", message = "Email o contraseña incorrectos." }, statusCode: StatusCodes.Status401Unauthorized);

        var result = await auth.LoginAsync(email, body.Password ?? "", cancellationToken);
        if (result is null)
        {
            return Results.Json(new { error = "invalid_credentials", message = "Email o contraseña incorrectos." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new VerifyResponse(result.SessionToken, result.User));
    }

    private static async Task<IResult> RegisterAsync(
        RegisterBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8)
            return Results.BadRequest(new { error = "invalid_password", message = "La contraseña debe tener al menos 8 caracteres." });

        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5 || !email.Contains('@'))
            return Results.BadRequest(new { error = "invalid_email", message = "Email inválido." });

        if (await auth.EmailHasRegisteredAccountAsync(email, cancellationToken))
            return Results.Conflict(new { error = "email_taken", message = "Ese email ya está registrado." });

        var username = (body.Username ?? "").Trim();
        if (!AuthUtils.IsValidUsername(username))
            return Results.BadRequest(new { error = "invalid_username", message = "Usuario inválido (3–32 caracteres, letras, números o _)." });

        if (await auth.UsernameHasRegisteredAccountAsync(username, cancellationToken))
            return Results.Conflict(new { error = "username_taken", message = "Ese nombre de usuario ya está en uso." });

        if (await auth.PhoneHasRegisteredAccountAsync(body.Phone, cancellationToken))
            return Results.Conflict(new { error = "phone_taken", message = "Ese teléfono ya está registrado." });

        var result = await auth.StartRegistrationAsync(
            body.Password!,
            email,
            username,
            body.Phone ?? "",
            cancellationToken);
        if (result is null)
            return Results.BadRequest(new { error = "registration_failed", message = "No se pudo iniciar el registro." });

        return Results.Ok(new RegisterStartResponse(
            result.RegistrationId,
            result.CodeLength,
            result.ExpiresInSeconds,
            result.DevMockCode));
    }

    private static async Task<IResult> RegisterVerifyPhoneAsync(
        VerifyRegistrationBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.VerifyRegistrationPhoneAsync(
            body.RegistrationId ?? "",
            body.Code ?? "",
            cancellationToken);
        if (result is null)
            return Results.Json(new { error = "invalid_code", message = "Código inválido o registro expirado." }, statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(new VerifyPhoneResponse(result.CodeLength, result.ExpiresInSeconds, result.DevMockCode));
    }

    private static async Task<IResult> RegisterVerifyEmailAsync(
        VerifyRegistrationBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.VerifyRegistrationEmailAsync(
            body.RegistrationId ?? "",
            body.Code ?? "",
            cancellationToken);
        if (result is null)
            return Results.Json(new { error = "invalid_code", message = "Código inválido o registro expirado." }, statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(new VerifyResponse(result.SessionToken, result.User));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < 8)
            return Results.BadRequest(new { error = "invalid_password", message = "La contraseña debe tener al menos 8 caracteres." });

        var email = AuthUtils.NormalizeEmail(body.Email);
        if (email.Length < 5)
            return Results.BadRequest(new { error = "invalid_email", message = "Email inválido." });

        if (!await auth.EmailHasRegisteredAccountAsync(email, cancellationToken))
            return Results.NotFound(new { error = "email_not_found", message = "No hay cuenta con ese email." });

        var result = await auth.RequestPasswordResetAsync(email, body.NewPassword!, cancellationToken);
        if (result is null)
            return Results.BadRequest(new { error = "reset_failed", message = "No se pudo solicitar el cambio." });

        return Results.Ok(new ForgotPasswordResponse(result.CodeLength, result.ExpiresInSeconds, result.DevMockCode));
    }

    private static async Task<IResult> ConfirmPasswordResetAsync(
        ConfirmPasswordResetBody body,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        var ok = await auth.ConfirmPasswordResetAsync(
            body.Email ?? "",
            body.Code ?? "",
            cancellationToken);
        if (!ok)
            return Results.Json(new { error = "invalid_code", message = "Código inválido o expirado." }, statusCode: StatusCodes.Status401Unauthorized);

        return Results.NoContent();
    }

    private static async Task<IResult> GetSessionAsync(
        HttpRequest request,
        IAuthService auth,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetUser(request, out var user) || user is null)
            return Results.Unauthorized();

        if (!string.IsNullOrWhiteSpace(user.Id))
        {
            var id = user.Id;
            var snapshot =
                await auth.GetProfileSnapshotByUserIdAsync(id, cancellationToken)
                ?? await auth.GetProfileSnapshotAsync(AuthUtils.PhoneDigitsFromSessionUser(user), cancellationToken);
            if (snapshot is not null &&
                auth.TrySyncSessionFromSnapshot(request.Headers.Authorization, snapshot, out var merged) &&
                merged is not null)
                user = merged;
        }

        return Results.Ok(new SessionResponse(user));
    }

    private static IResult Logout(HttpRequest request, IAuthService auth)
    {
        auth.RevokeSession(request.Headers.Authorization);
        return Results.NoContent();
    }

    private static bool IsRegisterMode(string? mode) =>
        string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase);
}
