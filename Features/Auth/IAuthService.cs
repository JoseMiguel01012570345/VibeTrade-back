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
}
