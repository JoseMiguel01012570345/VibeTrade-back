using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Infrastructure.Interfaces;

namespace VibeTrade.Backend.Infrastructure;

/// <summary>Gating por rol para endpoints administrativos (finanzas, estadísticas, usuarios).</summary>
public static class AdminAuthorization
{
    public sealed record RoleGateResult(string? UserId, IResult? Error)
    {
        public bool Ok => Error is null && UserId is not null;
    }

    /// <summary>
    /// Devuelve el id del usuario autenticado si posee alguno de los <paramref name="requiredRoles"/>;
    /// en caso contrario, un <see cref="IResult"/> 401/403 listo para retornar desde el endpoint.
    /// </summary>
    public static async Task<RoleGateResult> RequireRolesAsync(
        this ICurrentUserAccessor currentUser,
        HttpRequest request,
        IUserRoleService roles,
        CancellationToken cancellationToken,
        params string[] requiredRoles)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return new RoleGateResult(null, Results.Unauthorized());

        if (requiredRoles.Length > 0)
        {
            var allowed = await roles.HasAnyRoleAsync(userId, requiredRoles, cancellationToken).ConfigureAwait(false);
            if (!allowed)
                return new RoleGateResult(
                    null,
                    Results.Json(
                        new { error = "forbidden", message = "No tienes permisos para esta operación." },
                        statusCode: StatusCodes.Status403Forbidden));
        }

        return new RoleGateResult(userId, null);
    }
}
