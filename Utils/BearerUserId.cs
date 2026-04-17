using System.Text.Json;
using Microsoft.AspNetCore.Http;
using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Utils;

public static class BearerUserId
{
    public static string? FromRequest(IAuthService auth, HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authHdr))
            return null;
        return FromAuthorizationHeader(auth, authHdr.ToString());
    }

    public static string? FromAuthorizationHeader(IAuthService auth, string? authorizationHeader)
    {
        if (!auth.TryGetUserByToken(authorizationHeader, out var user))
            return null;
        if (!user.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return null;
        var id = idEl.GetString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
