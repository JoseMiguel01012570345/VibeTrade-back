namespace VibeTrade.Backend.Features.Trust.Interfaces;

public interface IAgreementCompletionTrustService
{
    Task TryApplyCompletionBonusesAsync(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default);
}
