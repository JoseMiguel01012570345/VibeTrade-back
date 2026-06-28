using MediatR;
using VibeTrade.Backend.Features.Trust.ApplyCompletionBonus;

namespace VibeTrade.Backend.Features.Trust;

public sealed class AgreementCompletionTrustService(IMediator mediator)
{
    public Task TryApplyCompletionBonusesAsync(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new ApplyCompletionBonusCommand(threadId, agreementId), cancellationToken);
}
