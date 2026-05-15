namespace VibeTrade.Backend.Features.Agreements.Interfaces;

public interface IAgreementServiceEvidenceService
{
    Task<(int StatusCode, IReadOnlyList<AgreementServicePaymentWithEvidenceDto>? Data)> ListAsync(
        string userId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error, ServiceEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        UpsertServiceEvidenceRequest body,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error)> DecideAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        DecideServiceEvidenceRequest body,
        CancellationToken cancellationToken);

    Task<(int StatusCode, string? Error)> RecordSellerPayoutAsync(
        string userId,
        string threadId,
        string agreementId,
        string paymentId,
        RecordSellerServicePayoutRequest body,
        CancellationToken cancellationToken);
}

