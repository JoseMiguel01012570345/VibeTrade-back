using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.SendMessage;

public sealed class SendMessageHandler(ChatServiceCore core) : IRequestHandler<SendMessageCommand, ChatMessageDto?>
{
    public Task<ChatMessageDto?> Handle(SendMessageCommand request, CancellationToken cancellationToken) =>
        core.PostMessageAsync(request.Request, cancellationToken);
}
