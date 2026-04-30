namespace VibeTrade.Backend.Data.Entities;

/// <summary>Pago (retenido/liberado) por servicio + cuota específica dentro de un acuerdo.</summary>
public sealed class AgreementServicePaymentRow
{
    public string Id { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public string ThreadId { get; set; } = "";

    public string BuyerUserId { get; set; } = "";

    /// <summary>Id del ítem de servicio dentro del acuerdo (TradeAgreementServiceItemRow.Id).</summary>
    public string ServiceItemId { get; set; } = "";

    public int EntryMonth { get; set; }

    public int EntryDay { get; set; }

    /// <summary>código Stripe minúsculas (usd, ars, …)</summary>
    public string Currency { get; set; } = "";

    public long AmountMinor { get; set; }

    /// <summary>held | released | refunded | failed</summary>
    public string Status { get; set; } = AgreementServicePaymentStatuses.Held;

    /// <summary>Referencia al cobro Stripe por moneda que cubrió este pago (opcional).</summary>
    public string? AgreementCurrencyPaymentId { get; set; }

    public AgreementCurrencyPaymentRow? AgreementCurrencyPayment { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReleasedAtUtc { get; set; }

    /// <summary>Stripe PaymentMethod (tarjeta) elegida por el vendedor (referencia/registro).</summary>
    public string? SellerPayoutPaymentMethodStripeId { get; set; }

    public DateTimeOffset? SellerPayoutRecordedAtUtc { get; set; }

    public string? SellerPayoutCardBrandSnapshot { get; set; }

    public string? SellerPayoutCardLast4Snapshot { get; set; }

    /// <summary>Stripe <c>Transfer</c> (tr_) hacia cuenta Connect cuando la liquidación se ejecutó en Stripe.</summary>
    public string? SellerPayoutStripeTransferId { get; set; }
}

public static class AgreementServicePaymentStatuses
{
    public const string Held = "held";
    public const string Released = "released";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

