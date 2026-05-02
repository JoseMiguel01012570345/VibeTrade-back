namespace VibeTrade.Backend.Data.Entities;

/// <summary>Mercadería incluida en un cobro por moneda (evita doble cobro de la misma línea).</summary>
public sealed class AgreementMerchandiseLinePaidRow
{
    public string Id { get; set; } = "";

    public string AgreementCurrencyPaymentId { get; set; } = "";

    public AgreementCurrencyPaymentRow AgreementCurrencyPayment { get; set; } = null!;

    /// <summary>Id de <see cref="TradeAgreementMerchandiseLineRow"/>.</summary>
    public string MerchandiseLineId { get; set; } = "";

    /// <summary>Misma moneda que <see cref="AgreementCurrencyPaymentRow.Currency"/> (minúsculas).</summary>
    public string Currency { get; set; } = "";

    public long AmountMinor { get; set; }
}
