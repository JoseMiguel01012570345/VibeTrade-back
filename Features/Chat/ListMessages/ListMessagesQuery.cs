using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.ListMessages;

public sealed record ListMessagesQuery(string UserId, string ThreadId)
    : IRequest<IReadOnlyList<ChatMessageDto>>;
