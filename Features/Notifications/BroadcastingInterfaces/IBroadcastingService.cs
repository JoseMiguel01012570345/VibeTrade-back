using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;

/// <summary>SignalR: grupos de hilo/usuario/oferta y envío a participantes del chat.</summary>
public interface IBroadcastingService : ISignalRBroadcastService
{
    Task HubSendToThreadParticipantsAsync(
        ChatThreadRow thread,
        string method,
        object payload,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetMessageRecipientUserIdsAsync(
        ChatThreadRow thread,
        string senderUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetThreadParticipantUserIdsForThreadRowAsync(
        ChatThreadRow thread,
        CancellationToken cancellationToken = default);

    Task NotifyThreadCreatedToBuyerAsync(ChatThreadDto dto, CancellationToken cancellationToken = default);

    Task NotifyThreadCreatedToUserAsync(string userId, ChatThreadDto dto, CancellationToken cancellationToken = default);

    Task BroadcastThreadCreatedToUsersAsync(
        IEnumerable<string> userIds,
        ChatThreadDto dto,
        CancellationToken cancellationToken = default);

    Task<bool> BroadcastParticipantLeftToOthersAsync(
        string leaverUserId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetThreadParticipantUserIdsAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>SignalR <c>messageCreated</c> a participantes del hilo.</summary>
    Task BroadcastChatMessageCreatedAsync(
        ChatThreadRow thread,
        ChatMessageDto messageDto,
        CancellationToken cancellationToken = default);

    /// <summary>Varios <c>messageCreated</c> (p. ej. sincronización masiva de respuestas QA).</summary>
    Task BroadcastChatMessagesCreatedAsync(
        IReadOnlyList<(ChatThreadRow Thread, ChatMessageDto Message)> items,
        CancellationToken cancellationToken = default);

    /// <summary>SignalR <c>messageStatusChanged</c> si el estado o recibos de grupo cambiaron.</summary>
    Task TryBroadcastChatMessageStatusChangedAsync(
        ChatThreadRow thread,
        string threadPersistedId,
        ChatMessageRow message,
        ChatMessageStatus statusBefore,
        string? groupReceiptsJsonBefore,
        CancellationToken cancellationToken = default);

    /// <summary>SignalR <c>peerPartyExitedChat</c> a participantes activos del hilo.</summary>
    Task BroadcastPeerPartyExitedChatAsync(
        ChatThreadRow thread,
        string threadPersistedId,
        string leaverUserId,
        string? partyExitedReason,
        DateTimeOffset? partyExitedAtUtc,
        bool leaverIsSeller,
        CancellationToken cancellationToken = default);

    /// <summary>Tras sincronizar respuestas QA: si el único contenido del hilo es ese lote, avisa <c>threadCreated</c> al vendedor.</summary>
    Task TryNotifySellersThreadCreatedAfterQaMessageInsertSyncAsync(
        IReadOnlyList<ChatMessageRow> hubRows,
        IReadOnlyList<ChatThreadRow> threads,
        Func<ChatThreadRow, CancellationToken, Task<ChatThreadDto>> mapThreadWithBuyerLabel,
        CancellationToken cancellationToken = default);
}
