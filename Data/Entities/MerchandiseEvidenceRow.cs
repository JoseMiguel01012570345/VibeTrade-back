namespace VibeTrade.Backend.Data.Entities;

/// <summary>Evidencia de cumplimiento asociada a un pago de línea de mercancía (retenido).</summary>
public sealed class MerchandiseEvidenceRow
{
    public string Id { get; set; } = "";

    public string AgreementMerchandiseLinePaidId { get; set; } = "";

    public AgreementMerchandiseLinePaidRow AgreementMerchandiseLinePaid { get; set; } = null!;

    public string SellerUserId { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Adjuntos persistidos (jsonb).</summary>
    public List<ServiceEvidenceAttachmentBody> Attachments { get; set; } = new();

    public string LastSubmittedText { get; set; } = "";

    public List<ServiceEvidenceAttachmentBody> LastSubmittedAttachments { get; set; } = new();

    public DateTimeOffset? LastSubmittedAtUtc { get; set; }

    /// <summary>draft | submitted | accepted | rejected</summary>
    public string Status { get; set; } = MerchandiseEvidenceStatuses.Draft;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? BuyerDecisionAtUtc { get; set; }
}

public static class MerchandiseEvidenceStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}
