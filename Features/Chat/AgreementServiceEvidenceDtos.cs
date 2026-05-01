using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat;

public sealed record ServiceEvidenceDto(
    string Id,
    string SellerUserId,
    string Text,
    IReadOnlyList<ServiceEvidenceAttachmentBody> Attachments,
    string LastSubmittedText,
    IReadOnlyList<ServiceEvidenceAttachmentBody> LastSubmittedAttachments,
    DateTimeOffset? LastSubmittedAtUtc,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? BuyerDecisionAtUtc);

public sealed record AgreementServicePaymentWithEvidenceDto(
    string Id,
    string ServiceItemId,
    int EntryMonth,
    int EntryDay,
    string CurrencyLower,
    long AmountMinor,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    ServiceEvidenceDto? Evidence,
    DateTimeOffset? SellerPayoutRecordedAtUtc,
    string? SellerPayoutCardBrand,
    string? SellerPayoutCardLast4,
    string? SellerPayoutStripeTransferId);

public sealed record UpsertServiceEvidenceRequest(
    string Text,
    IReadOnlyList<ServiceEvidenceAttachmentBody>? Attachments,
    bool Submit);

public sealed record DecideServiceEvidenceRequest(string Decision);

public sealed record RecordSellerServicePayoutRequest(string PaymentMethodId);

