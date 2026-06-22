namespace VibeTrade.Backend.Features.Auth.Dtos;

public sealed record RequestCodeBody(string Phone, string? Mode = null);

public sealed record RequestCodeResponse(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

public sealed record VerifyBody(string Phone, string Code, string? Mode = null);

public sealed record VerifyResponse(string SessionToken, SessionUser User);

public sealed record SessionResponse(SessionUser User);

public sealed record PatchProfileBody(
    string? Name,
    string? Username,
    string? Email,
    string? Instagram,
    string? Telegram,
    string? XAccount,
    string? AvatarUrl);

public sealed record PublicUserProfileResponse(
    string Id,
    string Name,
    string? AvatarUrl,
    int TrustScore);

public sealed record AddContactBody(string? Phone);

public sealed record LoginBody(string? Email, string? Password);

public sealed record RegisterBody(string? Password, string? Email, string? Username, string? Phone);

public sealed record VerifyRegistrationBody(string? RegistrationId, string? Code);

public sealed record ForgotPasswordBody(string? Email, string? NewPassword);

public sealed record ConfirmPasswordResetBody(string? Email, string? Code);

public sealed record RegisterStartResponse(
    string RegistrationId,
    int CodeLength,
    int ExpiresInSeconds,
    string? DevMockCode);

public sealed record VerifyPhoneResponse(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

public sealed record ForgotPasswordResponse(int CodeLength, int ExpiresInSeconds, string? DevMockCode);
