namespace VibeTrade.Backend.Data.Entities;

/// <summary>Cabecera legado por bloque de mercancías (0..1 por acuerdo).</summary>
public sealed class TradeAgreementMerchandiseMetaRow
{
    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public string Moneda { get; set; } = "";

    public string TipoEmbalaje { get; set; } = "";

    public string DevolucionesDesc { get; set; } = "";

    public string DevolucionQuienPaga { get; set; } = "";

    public string DevolucionPlazos { get; set; } = "";

    public string Regulaciones { get; set; } = "";
}
