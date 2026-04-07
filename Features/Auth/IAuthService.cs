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
}
