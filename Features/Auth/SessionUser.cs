using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Auth;

/// <summary>Usuario de sesión (<c>auth_sessions.userJson</c>): mismo shape que expone la API de auth.</summary>
public sealed class SessionUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("instagram")]
    public string? Instagram { get; set; }

    [JsonPropertyName("telegram")]
    public string? Telegram { get; set; }

    [JsonPropertyName("xAccount")]
    public string? XAccount { get; set; }

    [JsonPropertyName("trustScore")]
    public int? TrustScore { get; set; }

    public SessionUser Clone() =>
        new()
        {
            Id = Id,
            Phone = Phone,
            Name = Name,
            Email = Email,
            AvatarUrl = AvatarUrl,
            Instagram = Instagram,
            Telegram = Telegram,
            XAccount = XAccount,
            TrustScore = TrustScore,
        };
}
