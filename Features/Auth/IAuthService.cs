namespace VibeTrade.Backend.Features.Auth;

public sealed record RequestCodeResult(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

public sealed record VerifyResult(string SessionToken, SessionUser User);

public interface IAuthService
{
    RequestCodeResult RequestCode(string phoneRaw);

    Task<VerifyResult?> Verify(string phoneRaw, string code, CancellationToken cancellationToken);

    bool TryGetUserByToken(string? bearerToken, out SessionUser? user);

    bool RevokeSession(string? bearerToken);

    bool TrySetAvatarUrl(string? bearerToken, string avatarUrl, out SessionUser? updatedUser);

    bool TryPatchUserProfile(
        string? bearerToken,
        string? name,
        string? email,
        string? instagram,
        string? telegram,
        string? xAccount,
        string? avatarUrl,
        out SessionUser? updatedUser);

    bool TrySyncSessionFromSnapshot(string? bearerToken, UserProfileSnapshot snapshot, out SessionUser? updatedUser);

    bool TrySetSessionUserId(string? bearerToken, string userId, out SessionUser? updatedUser);
}
