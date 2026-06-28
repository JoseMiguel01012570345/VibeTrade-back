namespace VibeTrade.Backend.Features.Auth.Entities;

/// <summary>Código OTP pendiente para verificación por email (registro o reset).</summary>
public sealed class AuthPendingEmailOtpRow
{
    public string Key { get; set; } = "";

    /// <summary><c>register</c> o <c>password_reset</c>.</summary>
    public string Purpose { get; set; } = "";

    public string Code { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
