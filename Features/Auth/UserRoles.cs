using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Auth;

/// <summary>
/// Cálculo de roles efectivos: roles persistidos en la cuenta más los derivados del estado
/// (dueño de tienda => <see cref="RoleNames.SuperAdmin"/>; con afiliaciones => <see cref="RoleNames.Afiliado"/>).
/// Es la fuente de verdad tanto para el gating de endpoints como para lo expuesto en la sesión.
/// </summary>
public static class UserRoles
{
    public static async Task<List<string>> ComputeEffectiveRolesAsync(
        AppDbContext db,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length == 0)
            return new List<string>();

        var roles = new HashSet<string>(StringComparer.Ordinal);

        var persisted = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => u.Roles)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (persisted is not null)
        {
            foreach (var r in persisted)
            {
                var canonical = string.IsNullOrWhiteSpace(r) ? null : r.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(canonical))
                    roles.Add(canonical);
            }
        }

        var ownsStore = await db.Stores.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (ownsStore)
            roles.Add(RoleNames.SuperAdmin);

        var hasAffiliations = await db.Affiliates.AsNoTracking()
            .AnyAsync(a => a.OwnerUserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (hasAffiliations)
            roles.Add(RoleNames.Afiliado);

        return roles.ToList();
    }
}
