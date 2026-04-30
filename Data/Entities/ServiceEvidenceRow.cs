namespace VibeTrade.Backend.Data.Entities;

public sealed record ServiceEvidenceAttachmentBody(
    string Id,
    string Url,
    string FileName,
    string Kind);

/// <summary>Evidencia de cumplimiento asociada a un pago de servicio (retenido).</summary>
public sealed class ServiceEvidenceRow
{
    public string Id { get; set; } = "";

    public string AgreementServicePaymentId { get; set; } = "";

    public AgreementServicePaymentRow AgreementServicePayment { get; set; } = null!;

    public string SellerUserId { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Adjuntos persistidos (jsonb).</summary>
    public List<ServiceEvidenceAttachmentBody> Attachments { get; set; } = new();

    /// <summary>
    /// Snapshot de la última evidencia enviada al comprador (submit=true).
    /// Permite bloquear envíos repetidos si el vendedor vuelve a intentar enviar lo mismo.
    /// </summary>
    public string LastSubmittedText { get; set; } = "";

    /// <summary>Adjuntos del último envío (jsonb).</summary>
    public List<ServiceEvidenceAttachmentBody> LastSubmittedAttachments { get; set; } = new();

    public DateTimeOffset? LastSubmittedAtUtc { get; set; }

    /// <summary>draft | submitted | accepted | rejected</summary>
    public string Status { get; set; } = ServiceEvidenceStatuses.Draft;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? BuyerDecisionAtUtc { get; set; }
}

public static class ServiceEvidenceStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

