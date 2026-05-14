using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

/// <summary>Inserta un mensaje de chat ya validado (persistencia + notificaciones/hub vía <see cref="ChatService"/>).</summary>
public interface IChatMessageInserter
{
    Task<ChatMessageDto> InsertChatMessageAsync(
        ChatThreadRow thread,
        string senderUserId,
        ChatMessagePayload payloadObj,
        CancellationToken cancellationToken = default);
}
