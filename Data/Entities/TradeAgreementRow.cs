namespace VibeTrade.Backend.Data.Entities;

/// <summary>
/// Acuerdo comercial emitido por el vendedor (tienda) y aceptado o rechazado por el comprador.
/// Mercancías y servicios en tablas relacionadas (sin JSON en esquema).
/// </summary>
public sealed class TradeAgreementRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public ChatThreadRow Thread { get; set; } = null!;

    public string Title { get; set; } = "";

    /// <summary>UTC: misma semántica que <c>issuedAt</c> en el cliente (epoch ms).</summary>
    public DateTimeOffset IssuedAtUtc { get; set; }

    public string IssuedByStoreId { get; set; } = "";

    public string IssuerLabel { get; set; } = "";

    /// <summary><c>pending_buyer</c> | <c>accepted</c> | <c>rejected</c>.</summary>
    public string Status { get; set; } = "pending_buyer";

    public DateTimeOffset? RespondedAtUtc { get; set; }

    public string? RespondedByUserId { get; set; }

    public bool SellerEditBlockedUntilBuyerResponse { get; set; }

    /// <summary>
    /// Se pone true cuando el comprador acepta al menos una vez; no se borra si luego rechaza una revisión.
    /// Sirve para penalizar al vendedor si el rechazo llega después de una aceptación.
    /// </summary>
    public bool HadBuyerAcceptance { get; set; }

    public bool IncludeMerchandise { get; set; } = true;

    public bool IncludeService { get; set; } = true;

    public string? RouteSheetId { get; set; }

    public string? RouteSheetUrl { get; set; }

    /// <summary>Baja lógica: el acuerdo y el historial de chat se conservan; el cliente lo muestra como eliminado.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    public string? DeletedByUserId { get; set; }

    public ICollection<TradeAgreementMerchandiseLineRow> MerchandiseLines { get; set; } =
        new List<TradeAgreementMerchandiseLineRow>();

    public TradeAgreementMerchandiseMetaRow? MerchandiseMeta { get; set; }

    public ICollection<TradeAgreementServiceItemRow> ServiceItems { get; set; } =
        new List<TradeAgreementServiceItemRow>();

    public ICollection<TradeAgreementExtraFieldRow> ExtraFields { get; set; } =
        new List<TradeAgreementExtraFieldRow>();
}
