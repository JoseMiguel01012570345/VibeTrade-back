using System.Text.Json;

namespace VibeTrade.Backend.Features.Auth;

public sealed record RequestCodeResult(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

public sealed record VerifyResult(string SessionToken, JsonElement User);

public interface IAuthService
{
    RequestCodeResult RequestCode(string phoneRaw);

    VerifyResult? Verify(string phoneRaw, string code);

    bool TryGetUserByToken(string? bearerToken, out JsonElement user);

    bool RevokeSession(string? bearerToken);

    /// <summary>Actualiza el JSON del usuario en la sesión en memoria (p. ej. avatarUrl).</summary>
    bool TrySetAvatarUrl(string? bearerToken, string avatarUrl, out JsonElement updatedUser);

    /// <summary>Fusiona campos de perfil en el JSON de sesión; <c>null</c> = no modificar esa propiedad.</summary>
    bool TryPatchUserProfile(
        string? bearerToken,
        string? name,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        out JsonElement updatedUser);

    /// <summary>Reemplaza en la sesión los campos persistidos según el snapshot de BD (fuente de verdad tras GET session / PATCH).</summary>
    bool TrySyncSessionFromSnapshot(string? bearerToken, UserProfileSnapshot snapshot, out JsonElement updatedUser);

    /// <summary>
    /// Fuerza el <c>user.id</c> en la sesión. Se usa para mantener estable la identidad cuando el auth dev es in-memory.
    /// </summary>
    bool TrySetSessionUserId(string? bearerToken, string userId, out JsonElement updatedUser);
}
