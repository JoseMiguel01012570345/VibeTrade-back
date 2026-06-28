using MediatR;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Chat.AddParticipant;

public sealed record AddParticipantCommand(string CreatorUserId, IReadOnlyList<string> OtherUserIds)
    : IRequest<ChatThreadDto?>;
