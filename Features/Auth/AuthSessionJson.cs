using System.Text.Json;

namespace VibeTrade.Backend.Features.Auth;

internal static class AuthSessionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
