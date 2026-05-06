namespace VibeTrade.Backend.Features.Chat.Interfaces;

public interface IAgreementMerchandiseEvidenceService
{
    Task<(int StatusCode, IReadOnlyList<AgreementMerchandiseLinePaymentWithEvidenceDto>? Data)> ListAsync(
        string userId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error, MerchandiseEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        UpsertMerchandiseEvidenceRequest body,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error)> DecideAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        DecideMerchandiseEvidenceRequest body,
        CancellationToken cancellationToken);
}
