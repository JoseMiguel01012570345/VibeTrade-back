namespace VibeTrade.Backend.Features.Chat.Interfaces;

/// <summary>Gestión de mensajes de chat: post, listar, actualizar estado.</summary>
public interface IMessageHandlingService
{
    Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(string userId, string threadId, CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> PostMessageAsync(
        PostChatMessageArgs request,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto?> UpdateMessageStatusAsync(
        UpdateChatMessageStatusArgs request,
        CancellationToken cancellationToken = default);

    Task<int> AckAllPendingIncomingDeliveredAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
