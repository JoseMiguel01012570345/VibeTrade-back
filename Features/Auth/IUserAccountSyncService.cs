using System.Text.Json;

namespace VibeTrade.Backend.Features.Auth;

/// <summary>Persiste perfil de cuenta en PostgreSQL alineado a <c>database-model.md</c>.</summary>
public interface IUserAccountSyncService
{
    Task UpsertFromSessionUserAsync(JsonElement user, CancellationToken cancellationToken = default);

    Task SetAvatarUrlAsync(string userId, string avatarUrl, CancellationToken cancellationToken = default);

    Task<string?> GetAvatarUrlAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza campos de perfil persistidos; solo modifica propiedades no nulas.</summary>
    /// <param name="phoneDigitsForLookup">Si no hay fila con <paramref name="userId"/>, busca por <c>PhoneDigits</c> (misma lógica que el upsert de login).</param>
    Task PatchProfileAsync(
        string userId,
        string? displayName,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        string? phoneDigitsForLookup = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lee perfil persistido para fusionar en <c>GET session</c>.</summary>
    /// <param name="phoneDigits">Si no hay fila con <paramref name="userId"/>, busca por <c>PhoneDigits</c>.</param>
    Task<UserProfileSnapshot?> GetProfileSnapshotAsync(string? phoneDigits = null, CancellationToken cancellationToken = default);

    /// <summary>Indica si ya existe una cuenta con el teléfono normalizado (solo dígitos).</summary>
    Task<bool> PhoneHasRegisteredAccountAsync(string? phoneRaw, CancellationToken cancellationToken = default);
}
