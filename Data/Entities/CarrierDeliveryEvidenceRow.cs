namespace VibeTrade.Backend.Data.Entities;

/// <summary>Evidencia POD del transportista por tramo (paralelo conceptual a <see cref="ServiceEvidenceRow"/>).</summary>
public sealed class CarrierDeliveryEvidenceRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string RouteStopId { get; set; } = "";

    public string CarrierUserId { get; set; } = "";

    public string Text { get; set; } = "";

    public List<ServiceEvidenceAttachmentBody> Attachments { get; set; } = new();

    public string LastSubmittedText { get; set; } = "";

    public List<ServiceEvidenceAttachmentBody> LastSubmittedAttachments { get; set; } = new();

    public DateTimeOffset? LastSubmittedAtUtc { get; set; }

    /// <summary>draft | submitted | accepted | rejected</summary>
    public string Status { get; set; } = ServiceEvidenceStatuses.Draft;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecidedByUserId { get; set; }

    /// <summary>Plazo absoluto para enviar evidencia tras ceder ownership.</summary>
    public DateTimeOffset? DeadlineAtUtc { get; set; }
}
