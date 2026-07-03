namespace VibeTrade.Backend.Features.Auth.Interfaces;

/// <summary>Consulta de roles efectivos para gating de endpoints administrativos.</summary>
public interface IUserRoleService
{
    Task<IReadOnlyList<string>> GetEffectiveRolesAsync(string? userId, CancellationToken cancellationToken = default);

    Task<bool> HasAnyRoleAsync(string? userId, IEnumerable<string> roles, CancellationToken cancellationToken = default);
}
