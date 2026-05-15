namespace VibeTrade.Backend.Features.Chat.Interfaces;

/// <summary>Gestión de participantes de hilos: listar miembros.</summary>
public interface IParticipantManagementService
{
    Task<IReadOnlyList<ChatThreadMemberDto>?> ListSocialThreadMembersAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);
}
