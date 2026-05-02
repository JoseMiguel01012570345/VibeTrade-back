using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Logistics;

public sealed record UpsertCarrierDeliveryEvidenceRequest(string Text, List<ServiceEvidenceAttachmentBody>? Attachments, bool Submit);

public sealed record DecideCarrierDeliveryEvidenceRequest(string Decision);

public sealed record CarrierDeliveryEvidenceDto(
    string Id,
    string CarrierUserId,
    string Text,
    List<ServiceEvidenceAttachmentBody> Attachments,
    string LastSubmittedText,
    List<ServiceEvidenceAttachmentBody> LastSubmittedAttachments,
    DateTimeOffset? LastSubmittedAtUtc,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedByUserId,
    DateTimeOffset? DeadlineAtUtc);
