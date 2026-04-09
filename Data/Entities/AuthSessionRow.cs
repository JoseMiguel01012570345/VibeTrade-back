namespace VibeTrade.Backend.Data.Entities;

/// <summary>Token de sesión Bearer tras OTP; perfil de usuario en JSON (misma forma que el cliente).</summary>
public sealed class AuthSessionRow
{
    public string Token { get; set; } = "";

    public string UserJson { get; set; } = "{}";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
