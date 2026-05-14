namespace VibeTrade.Backend.Data.Entities;

/// <summary>
/// Campo libre del acuerdo (título + valor texto, imagen o documento subido a medios protegidos).
/// </summary>
public sealed class TradeAgreementExtraFieldRow
{
    public string Id { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public int SortOrder { get; set; }

    public string Title { get; set; } = "";

    /// <summary>text | image | document</summary>
    public string ValueKind { get; set; } = "text";

    public string? TextValue { get; set; }

    /// <summary>URL estable (p. ej. /api/v1/media/{id}).</summary>
    public string? MediaUrl { get; set; }

    public string? FileName { get; set; }
}
