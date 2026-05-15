using System.Text.Json;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth;

public static class AuthUtils
{
    public static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
    }

    /// <summary>Extrae el token de un encabezado <c>Authorization: Bearer …</c>.</summary>
    public static string? TryParseBearerToken(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return null;
        const string p = "Bearer ";
        if (!authorization.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            return null;
        return authorization[p.Length..].Trim();
    }

    /// <summary>Solo dígitos del teléfono de sesión, o <c>null</c> si no hay teléfono o no queda ningún dígito.</summary>
    public static string? PhoneDigitsFromSessionUser(SessionUser? user)
    {
        if (string.IsNullOrEmpty(user?.Phone))
            return null;
        var d = DigitsOnly(user.Phone);
        return d.Length == 0 ? null : d;
    }

    public static SessionUser CreateSessionUserForVerifiedPhone(string phoneDigits, UserProfileSnapshot? profile)
    {
        if (profile is null)
        {
            return new SessionUser
            {
                Id = phoneDigits,
                Phone = phoneDigits,
                Name = "Usuario sin nombre",
            };
        }

        return new SessionUser
        {
            Id = phoneDigits,
            Phone = phoneDigits,
            Name = profile.DisplayName,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl,
            Instagram = profile.Instagram,
            Telegram = profile.Telegram,
            XAccount = profile.XAccount,
        };
    }
}
