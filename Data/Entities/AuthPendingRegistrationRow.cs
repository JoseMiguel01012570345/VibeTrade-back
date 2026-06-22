namespace VibeTrade.Backend.Data.Entities;

/// <summary>Registro en curso antes de crear la cuenta definitiva.</summary>
public sealed class AuthPendingRegistrationRow
{
    public string RegistrationId { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string Email { get; set; } = "";

    public string Username { get; set; } = "";

    public string PhoneDigits { get; set; } = "";

    public string PhoneDisplay { get; set; } = "";

    public bool PhoneVerified { get; set; }

    public bool EmailVerified { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
