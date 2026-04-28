namespace VibeTrade.Backend.Features.Auth;

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
