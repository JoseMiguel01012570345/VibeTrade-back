using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Infrastructure;

public interface ICurrentUserAccessor
{
    bool TryGetUser(HttpRequest request, out SessionUser? user);

    string? GetUserId(HttpRequest request);
}

public sealed class CurrentUserAccessor(IAuthService auth) : ICurrentUserAccessor
{
    public bool TryGetUser(HttpRequest request, out SessionUser? user)
    {
        user = null;
        if (!request.Headers.TryGetValue("Authorization", out var authorization))
            return false;
        return auth.TryGetUserByToken(authorization.ToString(), out user);
    }

    public string? GetUserId(HttpRequest request)
    {
        if (!TryGetUser(request, out var user) || user is null)
            return null;
        var id = user.Id;
        if (string.IsNullOrWhiteSpace(id))
            return null;
        id = id.Trim();
        return id.Length == 0 ? null : id;
    }
}
