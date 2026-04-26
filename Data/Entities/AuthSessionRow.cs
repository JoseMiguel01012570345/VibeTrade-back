using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>Token de sesión Bearer tras OTP; perfil materializado (jsonb: <c>UserJson</c>).</summary>
public sealed class AuthSessionRow
{
    public string Token { get; set; } = "";

    public SessionUser User { get; set; } = new();

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
