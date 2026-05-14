using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Chat.Core;

public sealed partial class ChatService
{
    Task<ChatMessageDto> IChatMessageInserter.InsertChatMessageAsync(
        ChatThreadRow thread,
        string senderUserId,
        ChatMessagePayload payloadObj,
        CancellationToken cancellationToken) =>
        InsertChatMessageAsync(thread, senderUserId, payloadObj, cancellationToken);
}
