namespace VibeTrade.Backend.Data.Entities;

/// <summary>Un cobro Stripe por moneda asociado a un acuerdo (mercancía + servicios + tramos que usan esa moneda).</summary>
public sealed class AgreementCurrencyPaymentRow
{
    public string Id { get; set; } = "";

    public string TradeAgreementId { get; set; } = "";

    public TradeAgreementRow TradeAgreement { get; set; } = null!;

    public string ThreadId { get; set; } = "";

    public string BuyerUserId { get; set; } = "";

    /// <summary>código Stripe minúsculas (usd, ars, …)</summary>
    public string Currency { get; set; } = "";

    public long SubtotalAmountMinor { get; set; }

    public long ClimateAmountMinor { get; set; }

    public long StripeFeeAmountMinor { get; set; }

    public long TotalAmountMinor { get; set; }

    public string? StripePaymentIntentId { get; set; }

    /// <summary><see cref="AgreementPaymentStatuses"/></summary>
    public string Status { get; set; } = AgreementPaymentStatuses.Pending;

    public string? PaymentMethodStripeId { get; set; }

    public string? StripeErrorMessage { get; set; }

    /// <summary>Clave cliente para idempotencia (header Idempotency-Key).</summary>
    public string? ClientIdempotencyKey { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC: resultado final cobro.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ClientSecretForConfirmation { get; set; }

    /// <summary>Cuotas aplicadas por tramo en este cobro.</summary>
    public ICollection<AgreementRouteLegPaidRow> RouteLegPaids { get; set; } =
        new List<AgreementRouteLegPaidRow>();

    /// <summary>Líneas de mercadería incluidas en este cobro.</summary>
    public ICollection<AgreementMerchandiseLinePaidRow> MerchandiseLinePaids { get; set; } =
        new List<AgreementMerchandiseLinePaidRow>();
}

public static class AgreementPaymentStatuses
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    /// <summary>Reembolso total registrado en Stripe (p. ej. salida del vendedor en acuerdos solo servicios).</summary>
    public const string Refunded = "refunded";
    // Pago iniciado pero requiere 3DS u otra confirmación cliente.
    public const string RequiresConfirmation = "requires_confirmation";
}
