using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat;

public sealed record MerchandiseEvidenceDto(
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

public sealed record AgreementMerchandiseLinePaymentWithEvidenceDto(
    string Id,
    string MerchandiseLineId,
    string CurrencyLower,
    long AmountMinor,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    MerchandiseEvidenceDto? Evidence);

public sealed record UpsertMerchandiseEvidenceRequest(
    string Text,
    IReadOnlyList<ServiceEvidenceAttachmentBody>? Attachments,
    bool Submit);

public sealed record DecideMerchandiseEvidenceRequest(string Decision);
