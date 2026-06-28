using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.SendMessage;

public sealed record SendMessageCommand(PostChatMessageArgs Request) : IRequest<ChatMessageDto?>;
