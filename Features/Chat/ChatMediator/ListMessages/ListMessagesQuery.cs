using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.ChatMediator.ListMessages;

public sealed record ListMessagesQuery(string UserId, string ThreadId)
    : IRequest<IReadOnlyList<ChatMessageDto>>;
