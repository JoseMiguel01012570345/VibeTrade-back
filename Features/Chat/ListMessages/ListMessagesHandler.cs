using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.ListMessages;

public sealed class ListMessagesHandler(ChatServiceCore core)
    : IRequestHandler<ListMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    public Task<IReadOnlyList<ChatMessageDto>> Handle(
        ListMessagesQuery request,
        CancellationToken cancellationToken) =>
        core.ListMessagesAsync(request.UserId, request.ThreadId, cancellationToken);
}
