namespace VibeTrade.Backend.Features.Chat;

/// <summary>Gestión de hilos de chat: crear, listar, borrar, soft-leave.</summary>
public interface IThreadManagementService
{
    Task<ChatThreadDto?> CreateOrGetThreadForBuyerAsync(
        string buyerUserId,
        string offerId,
        bool purchaseIntent = true,
        bool forceNewThread = false,
        CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> CreateSocialGroupThreadAsync(
        string creatorUserId,
        IReadOnlyList<string> otherUserIds,
        CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> GetThreadIfVisibleAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> GetThreadByOfferIfVisibleAsync(string userId, string offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatThreadSummaryDto>> ListThreadsForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteThreadAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<PartySoftLeaveResult> SoftLeaveThreadAsPartyAsync(
        PartySoftLeaveArgs request,
        CancellationToken cancellationToken = default);

    Task<ChatThreadDto?> PatchSocialGroupTitleAsync(
        string userId,
        string threadId,
        string? title,
        CancellationToken cancellationToken = default);
}
