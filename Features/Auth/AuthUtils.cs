using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth;

public static class AuthUtils
{
    private static readonly PasswordHasher<object> PasswordHasher = new();
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

        return CreateSessionUserFromSnapshot(
            profile,
            FormatPhoneForDisplay(profile.PhoneDisplay, profile.PhoneDigits) ?? phoneDigits);
    }

    public static SessionUser CreateSessionUserFromSnapshot(UserProfileSnapshot profile, string? phoneDisplay = null) =>
        new()
        {
            Id = profile.Id,
            Phone = phoneDisplay ?? FormatPhoneForDisplay(profile.PhoneDisplay, profile.PhoneDigits) ?? profile.PhoneDigits,
            Name = profile.DisplayName,
            Username = profile.Username,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl,
            Instagram = profile.Instagram,
            Telegram = profile.Telegram,
            XAccount = profile.XAccount,
            TrustScore = profile.TrustScore,
        };

    public static string NormalizeEmail(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant();

    public static string? NormalizeUsername(string? raw)
    {
        var u = (raw ?? "").Trim();
        return u.Length == 0 ? null : u;
    }

    public static bool IsValidUsername(string username)
    {
        if (username.Length is < 3 or > 32)
            return false;
        return username.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    public static string? FormatPhoneForDisplay(string? phoneDisplay, string? phoneDigits = null)
    {
        var raw = (phoneDisplay ?? phoneDigits ?? "").Trim();
        if (raw.Length == 0)
            return phoneDigits;
        return raw.StartsWith('+') ? raw : $"+{raw}";
    }

    public static string HashPassword(string password) =>
        PasswordHasher.HashPassword(null!, password);

    public static bool VerifyPassword(string password, string hash) =>
        PasswordHasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
