using Microsoft.AspNetCore.Http;
using VibeTrade.Backend.Features.Payments.Dtos;

namespace VibeTrade.Backend.Features.Payments;

/// <summary>URL pública de referencia para políticas / precios de procesamiento Stripe (PDF y chat).</summary>
public static class StripePricing
{
    public const string PricingPage = "https://stripe.com/pricing";
}

/// <summary>Utilidades compartidas del feature de pagos.</summary>
public static class PaymentUtils
{
    public static (int StatusCode, object? Problem, CreatePaymentIntentResult? Data) Err(
        int status,
        string code,
        string message) =>
        (status, new { error = code, message }, null);

    public static (int StatusCode, object? Problem, CreatePaymentIntentResult? Data) Ok(
        CreatePaymentIntentResult data) =>
        (StatusCodes.Status200OK, null, data);
}
