using MediatR;
using VibeTrade.Backend.Features.Trust.TrustMediator.ApplyCompletionBonus;

namespace VibeTrade.Backend.Features.Trust;

public sealed class AgreementCompletionTrustService(IMediator mediator) : IAgreementCompletionTrustService
{
    public Task TryApplyCompletionBonusesAsync(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new ApplyCompletionBonusCommand(threadId, agreementId), cancellationToken);
}
