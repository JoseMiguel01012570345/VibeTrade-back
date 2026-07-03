using VibeTrade.Backend.Features.Users.Dtos;

namespace VibeTrade.Backend.Features.Users.Interfaces;

public interface IUsersAdminService
{
    Task<IReadOnlyList<AdminUserDto>> ListAsync(string? search, CancellationToken cancellationToken);

    Task<(AdminUserDto? Value, UsersOpError? Error)> CreateAsync(CreateUserRequest body, CancellationToken cancellationToken);

    Task<(AdminUserDto? Value, UsersOpError? Error)> UpdateAsync(string id, UpdateUserRequest body, CancellationToken cancellationToken);

    Task<(AdminUserDto? Value, UsersOpError? Error)> SetRolesAsync(string id, SetUserRolesRequest body, CancellationToken cancellationToken);

    Task<UsersOpError?> SetPasswordAsync(string id, SetUserPasswordRequest body, CancellationToken cancellationToken);

    Task<UsersOpError?> DeleteAsync(string id, string requestingUserId, CancellationToken cancellationToken);
}
