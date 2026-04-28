namespace VibeTrade.Backend.Data.Entities;

public sealed class TradeAgreementMerchandiseLineRow
{
    public string Id { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public int SortOrder { get; set; }

    public string? LinkedStoreProductId { get; set; }

    public string Tipo { get; set; } = "";

    public string Cantidad { get; set; } = "";

    public string ValorUnitario { get; set; } = "";

    /// <summary>nuevo | usado | reacondicionado</summary>
    public string Estado { get; set; } = "nuevo";

    public string Descuento { get; set; } = "";

    public string Impuestos { get; set; } = "";

    public string Moneda { get; set; } = "";

    public string TipoEmbalaje { get; set; } = "";

    public string DevolucionesDesc { get; set; } = "";

    public string DevolucionQuienPaga { get; set; } = "";

    public string DevolucionPlazos { get; set; } = "";

    public string Regulaciones { get; set; } = "";
}
