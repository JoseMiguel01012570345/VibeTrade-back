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

    public string TradeAgreementId { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string BuyerUserId { get; set; } = "";

    /// <summary><see cref="AgreementMerchandiseLinePaidStatuses"/></summary>
    public string Status { get; set; } = AgreementMerchandiseLinePaidStatuses.Held;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReleasedAtUtc { get; set; }
}

public static class AgreementMerchandiseLinePaidStatuses
{
    public const string Held = "held";
    public const string Released = "released";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}
