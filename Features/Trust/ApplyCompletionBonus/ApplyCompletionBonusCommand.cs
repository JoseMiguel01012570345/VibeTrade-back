using MediatR;

namespace VibeTrade.Backend.Features.Trust.ApplyCompletionBonus;

public sealed record ApplyCompletionBonusCommand(string ThreadId, string AgreementId) : IRequest;
