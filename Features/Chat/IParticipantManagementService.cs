namespace VibeTrade.Backend.Features.Chat;

/// <summary>Gestión de participantes de hilos: listar miembros, participantes.</summary>
public interface IParticipantManagementService
{
    Task<IReadOnlyList<ChatThreadMemberDto>?> ListSocialThreadMembersAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetThreadParticipantUserIdsAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    Task<bool> BroadcastParticipantLeftToOthersAsync(
        string leaverUserId,
        string threadId,
        CancellationToken cancellationToken = default);
}
