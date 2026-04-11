namespace VibeTrade.Backend.Features.Auth;

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

    Task<bool> RemoveAsync(string ownerUserId, string contactUserId, CancellationToken cancellationToken = default);
}
