using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth.Interfaces;

namespace VibeTrade.Backend.Features.Auth;

public sealed class UserRoleService(AppDbContext db) : IUserRoleService
{
    public async Task<IReadOnlyList<string>> GetEffectiveRolesAsync(
        string? userId,
        CancellationToken cancellationToken = default) =>
        await UserRoles.ComputeEffectiveRolesAsync(db, userId, cancellationToken).ConfigureAwait(false);

    public async Task<bool> HasAnyRoleAsync(
        string? userId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken = default)
    {
        var wanted = roles as ICollection<string> ?? roles.ToList();
        if (wanted.Count == 0)
            return false;
        var effective = await GetEffectiveRolesAsync(userId, cancellationToken).ConfigureAwait(false);
        return effective.Any(wanted.Contains);
    }
}
