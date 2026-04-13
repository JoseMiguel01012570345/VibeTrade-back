using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Infrastructure;

/// <summary>
/// Exige <c>Authorization: Bearer &lt;token&gt;</c> válido (sesión en base de datos) para <c>/api/v1/*</c>,
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
        // GET /api/v1/market/stores/search (búsqueda pública)
        if (HttpMethods.IsGet(method) &&
            path.Equals("/api/v1/market/stores/search", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/v1/market/catalog-categories (categorías públicas)
        if (HttpMethods.IsGet(method) &&
            path.Equals("/api/v1/market/catalog-categories", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/v1/bootstrap/guest (bootstrap público invitado)
        if (HttpMethods.IsGet(method) &&
            path.Equals("/api/v1/bootstrap/guest", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/v1/recommendations/guest (recomendaciones público invitado)
        if (HttpMethods.IsGet(method) &&
            path.Equals("/api/v1/recommendations/guest", StringComparison.OrdinalIgnoreCase))
            return true;

        // POST /api/v1/recommendations/guest/interactions (señales de invitado)
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/v1/recommendations/guest/interactions", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/v1/media/{id} (descarga pública de media por id)
        if (HttpMethods.IsGet(method) &&
            path.StartsWithSegments("/api/v1/media", StringComparison.OrdinalIgnoreCase))
            return true;

        // POST /api/v1/market/inquiries (consultas públicas; anon permitido)
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/v1/market/inquiries", StringComparison.OrdinalIgnoreCase))
            return true;

        // POST /api/v1/market/stores/{storeId}/detail (detalle público; carga bajo demanda)
        if (HttpMethods.IsPost(method) &&
            path.StartsWithSegments("/api/v1/market/stores", StringComparison.OrdinalIgnoreCase) &&
            path.Value?.EndsWith("/detail", StringComparison.OrdinalIgnoreCase) == true)
            return true;

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
