namespace VibeTrade.Backend.Features.Payments.Dtos;

public static class PaymentIntentKinds
{
    /// <summary>Cobro de acuerdo en chat: importe = subtotal del desglose de checkout (servidor).</summary>
    public const string AgreementCheckout = "agreement_checkout";
}

public sealed record PaymentGatewayConfigDto(bool Enabled, string GatewayId, bool SimulatedMode);

public sealed record SavedCardPaymentMethodDto(
    string Id,
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear,
    string? Country);

public sealed record CreateSetupIntentResult(string ClientSecret);

/// <summary>Cuota de servicio elegida (misma forma que checkout / execute).</summary>
public sealed record AgreementCheckoutPaymentIntentItemDto(string ServiceItemId, int EntryMonth, int EntryDay);

/// <summary>
/// Identifica el cobro; el monto y la descripción los calcula el servidor.
/// Para producción con persistencia y recibo use el POST de pagos del acuerdo en el hilo de chat.
/// </summary>
public sealed record CreatePaymentIntentBody(
    string Kind,
    string? ThreadId,
    string? AgreementId,
    string? Currency,
    string? PaymentMethodId,
    IReadOnlyList<AgreementCheckoutPaymentIntentItemDto>? SelectedServicePayments,
    IReadOnlyList<string>? SelectedRoutePathIds);

public sealed record CreatePaymentIntentResult(
    string ClientSecret,
    bool PaymentSkipped = false,
    long? AmountMinor = null,
    string? Currency = null);
