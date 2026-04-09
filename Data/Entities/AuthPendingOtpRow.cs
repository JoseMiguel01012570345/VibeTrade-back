namespace VibeTrade.Backend.Data.Entities;

/// <summary>Código OTP solicitado hasta verificación o expiración (clave: dígitos del teléfono).</summary>
public sealed class AuthPendingOtpRow
{
    public string PhoneDigits { get; set; } = "";

    public string Code { get; set; } = "";

    public int CodeLength { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
