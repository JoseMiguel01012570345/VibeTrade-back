namespace VibeTrade.Backend.Features.Auth.Dtos;

public sealed record RequestCodeResult(int CodeLength, int ExpiresInSeconds, string? DevMockCode);

public sealed record VerifyResult(string SessionToken, SessionUser User);

public sealed record UserContactDto(
    string UserId,
    string DisplayName,
    string? PhoneDisplay,
    string? PhoneDigits,
    DateTimeOffset CreatedAt);

/// <summary>Usuario registrado resuelto por teléfono (sin fila de agenda).</summary>
public sealed record PlatformUserByPhoneDto(
    string UserId,
    string DisplayName,
    string? PhoneDisplay,
    string? PhoneDigits);

/// <summary>Campos persistidos para fusionar con la sesión de token.</summary>
public sealed record UserProfileSnapshot(
    string Id,
    string DisplayName,
    string? Email,
    string? AvatarUrl,
    string? Instagram,
    string? Telegram,
    string? XAccount,
    int TrustScore = 0);

/// <summary>País habilitado para registro / inicio de sesión por SMS (selector de prefijo).</summary>
public sealed record SignInCountryDto(string Name, string Code, string Dial, string Flag);
