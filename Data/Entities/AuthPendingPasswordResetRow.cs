namespace VibeTrade.Backend.Data.Entities;

/// <summary>Contraseña nueva pendiente de confirmación por email.</summary>
public sealed class AuthPendingPasswordResetRow
{
    public string Email { get; set; } = "";

    public string NewPasswordHash { get; set; } = "";

    public string Code { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
