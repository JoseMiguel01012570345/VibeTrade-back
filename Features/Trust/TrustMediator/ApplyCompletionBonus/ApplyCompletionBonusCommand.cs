using MediatR;

namespace VibeTrade.Backend.Features.Trust.TrustMediator.ApplyCompletionBonus;

public sealed record ApplyCompletionBonusCommand(string ThreadId, string AgreementId) : IRequest;
