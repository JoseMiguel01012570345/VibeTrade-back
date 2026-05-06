using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat.Interfaces;

/// <summary>Control de acceso a hilos: verificar visibilidad y permisos.</summary>
public interface IThreadAccessControlService
{
    Task<bool> UserCanAccessThreadRowAsync(
        string userId,
        ChatThreadRow thread,
        CancellationToken cancellationToken = default);
}
