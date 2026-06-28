using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.AddParticipant;

public sealed class AddParticipantHandler(ChatServiceCore core) : IRequestHandler<AddParticipantCommand, ChatThreadDto?>
{
    public Task<ChatThreadDto?> Handle(AddParticipantCommand request, CancellationToken cancellationToken) =>
        core.CreateSocialGroupThreadAsync(request.CreatorUserId, request.OtherUserIds, cancellationToken);
}
