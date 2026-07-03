namespace VibeTrade.Backend.Features.Users.Dtos;

/// <summary>Fila de usuario expuesta al panel de administración (nunca incluye el hash de contraseña).</summary>
public sealed record AdminUserDto(
    string Id,
    string DisplayName,
    string? Email,
    string? Username,
    string? PhoneDisplay,
    IReadOnlyList<string> Roles,
    int TrustScore,
    bool OwnsStore,
    DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(string? Email, string? Password, string? DisplayName, string? Phone);

public sealed record UpdateUserRequest(string? DisplayName, string? Email);

/// <summary>Roles a asignar (etiquetas como "Administrador"/"Almacén"/"Afiliado" o ids canónicos).</summary>
public sealed record SetUserRolesRequest(List<string>? Roles);

public sealed record SetUserPasswordRequest(string? NewPassword);

public sealed record UsersOpError(string Code, string Message);
