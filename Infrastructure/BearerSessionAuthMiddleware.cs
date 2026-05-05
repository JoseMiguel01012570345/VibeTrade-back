using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Infrastructure;

/// <summary>
/// Exige <c>Authorization: Bearer &lt;token&gt;</c> válido (sesión en base de datos) para <c>/api/v1/*</c>,
/// salvo rutas anónimas del login y preflight CORS.
/// </summary>
public sealed class BearerSessionAuthMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiV1Prefix = new("/api/v1");
    private static readonly (string, string)[] AnonymousApi = new[] {
        ("GET", "/api/v1/market/stores/search"),
        ("GET", "/api/v1/market/stores/autocomplete"),
        // Ficha pública / QA (MarketController [AllowAnonymous]).
        ("GET", "/api/v1/market/offers"),
        ("GET", "/api/v1/market/catalog-categories"),
        ("GET", "/api/v1/bootstrap/guest"),
        ("GET", "/api/v1/recommendations/guest"),
        ("GET", "/api/v1/auth/sign-in-countries"),
        ("GET", "/api/v1/media"),
        ("POST", "/api/v1/recommendations/guest/interactions"),
        ("POST", "/api/v1/market/inquiries"),
        ("POST", "/api/v1/auth/request-code"),
        ("POST", "/api/v1/auth/verify"),
        ("POST", "/api/v1/market/stores"),
        ("GET", "/api/v1/market/currencies"),
        ("GET", "/api/v1/routing/osrm"),
    };

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
        foreach (var (m, p) in AnonymousApi)
        {
            if (method == m &&
                path.StartsWithSegments(new PathString(p)))
                return true;
        }
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
