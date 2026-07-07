using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.ChatMediator.SendMessage;

public sealed record SendMessageCommand(PostChatMessageArgs Request) : IRequest<ChatMessageDto?>;
