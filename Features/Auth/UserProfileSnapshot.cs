namespace VibeTrade.Backend.Features.Auth;

/// <summary>Campos persistidos para fusionar con la sesión en memoria.</summary>
public sealed record UserProfileSnapshot(
    string DisplayName,
    string? Email,
    string? AvatarUrl,
    string? Instagram,
    string? Telegram,
    string? XAccount);
