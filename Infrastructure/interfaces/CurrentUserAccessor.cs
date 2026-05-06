using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Infrastructure.Interfaces;

public interface ICurrentUserAccessor
{
    bool TryGetUser(HttpRequest request, out SessionUser? user);

    string? GetUserId(HttpRequest request);
}
