namespace VibeTrade.Backend.Features.Auth.Interfaces;

public sealed record UserContactDto(
    string UserId,
    string DisplayName,
    string? PhoneDisplay,
    string? PhoneDigits,
    DateTimeOffset CreatedAt);

public interface IUserContactsService
{
    Task<IReadOnlyList<UserContactDto>> ListAsync(string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normaliza el teléfono, busca cuenta registrada y añade el contacto.
    /// </summary>
    /// <exception cref="InvalidOperationException">Teléfono vacío, no registrado, es el propio usuario o duplicado.</exception>
    Task<UserContactDto> AddByPhoneAsync(string ownerUserId, string phoneRaw, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca una cuenta por número sin persistir en la agenda (misma validación que agregar contacto, sin duplicados).
    /// </summary>
    /// <returns><c>null</c> si el número no está registrado.</returns>
    /// <exception cref="InvalidOperationException">Teléfono vacío o es el propio usuario.</exception>
    Task<PlatformUserByPhoneDto?> ResolveByPhoneAsync(string requesterUserId, string phoneRaw, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string ownerUserId, string contactUserId, CancellationToken cancellationToken = default);
}

/// <summary>Usuario registrado resuelto por teléfono (sin fila de agenda).</summary>
public sealed record PlatformUserByPhoneDto(
    string UserId,
    string DisplayName,
    string? PhoneDisplay,
    string? PhoneDigits);
