using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Infrastructure;

/// <summary>
/// Exige <c>Authorization: Bearer &lt;token&gt;</c> válido (sesión en memoria) para <c>/api/v1/*</c>,
/// salvo rutas anónimas del login y preflight CORS.
/// </summary>
public sealed class BearerSessionAuthMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiV1Prefix = new("/api/v1");

    public async Task InvokeAsync(HttpContext context, IAuthService auth)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path;
        if (!path.StartsWithSegments(ApiV1Prefix))
        {
            await next(context);
            return;
        }

        if (AllowsAnonymousApi(path, context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authorization) ||
            string.IsNullOrWhiteSpace(authorization))
        {
            await WriteUnauthorizedAsync(context, "Falta el encabezado Authorization.");
            return;
        }

        if (!auth.TryGetUserByToken(authorization, out _))
        {
            await WriteUnauthorizedAsync(context, "Token de sesión inválido o expirado.");
            return;
        }

        await next(context);
    }

    private static bool AllowsAnonymousApi(PathString path, string method)
    {
        // POST /api/v1/auth/request-code
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/v1/auth/request-code", StringComparison.OrdinalIgnoreCase))
            return true;

        // POST /api/v1/auth/verify
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/v1/auth/verify", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/v1/auth/sign-in-countries
        if (HttpMethods.IsGet(method) &&
            path.Equals("/api/v1/auth/sign-in-countries", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new { error = "unauthorized", message },
            cancellationToken: context.RequestAborted);
    }
}
