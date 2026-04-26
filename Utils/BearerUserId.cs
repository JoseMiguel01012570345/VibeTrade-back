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
        if (!auth.TryGetUserByToken(authorizationHeader, out var user) || user is null)
            return null;
        var id = user.Id;
        if (string.IsNullOrWhiteSpace(id))
            return null;
        id = id.Trim();
        return id.Length == 0 ? null : id;
    }
}
