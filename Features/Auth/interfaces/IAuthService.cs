using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.Interfaces;

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

    Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<RegisterStartResult?> StartRegistrationAsync(
        string password,
        string email,
        string phoneRaw,
        CancellationToken cancellationToken);

    Task<VerifyPhoneResult?> VerifyRegistrationPhoneAsync(
        string registrationId,
        string code,
        CancellationToken cancellationToken);

    Task<VerifyResult?> VerifyRegistrationEmailAsync(
        string registrationId,
        string code,
        CancellationToken cancellationToken);

    Task<ForgotPasswordResult?> RequestPasswordResetAsync(
        string email,
        string newPassword,
        CancellationToken cancellationToken);

    Task<bool> ConfirmPasswordResetAsync(string email, string code, CancellationToken cancellationToken);

    Task UpsertFromSessionUserAsync(SessionUser user, CancellationToken cancellationToken = default);

    Task SetAvatarUrlAsync(string userId, string avatarUrl, CancellationToken cancellationToken = default);

    Task<string?> GetAvatarUrlAsync(string userId, CancellationToken cancellationToken = default);

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

    Task<UserProfileSnapshot?> GetProfileSnapshotAsync(string? phoneDigits = null, CancellationToken cancellationToken = default);

    Task<UserProfileSnapshot?> GetProfileSnapshotByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> PhoneHasRegisteredAccountAsync(string? phoneRaw, CancellationToken cancellationToken = default);

    Task<bool> EmailHasRegisteredAccountAsync(string? email, CancellationToken cancellationToken = default);

    Task<string?> TrySetUsernameAsync(
        string userId,
        string username,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserContactDto>> ListContactsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default);

    Task<UserContactDto> AddContactByPhoneAsync(
        string ownerUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default);

    Task<PlatformUserByPhoneDto?> ResolveContactByPhoneAsync(
        string requesterUserId,
        string phoneRaw,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveContactAsync(
        string ownerUserId,
        string contactUserId,
        CancellationToken cancellationToken = default);
}
