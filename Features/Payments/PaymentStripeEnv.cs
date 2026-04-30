namespace VibeTrade.Backend.Features.Payments;

/// <summary>LECTURA unificada de env Stripe (clave restringida, secreta, skip intents).</summary>
public static class PaymentStripeEnv
{
    public static string? StripeRestrictedKey() =>
        (Environment.GetEnvironmentVariable("STRIPE_RESTRICTED_KEY") ?? "").Trim() is { Length: > 0 } s ? s : null;

    public static string? StripeSecretKey() =>
        (Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? "").Trim() is { Length: > 0 } s ? s : null;

    /// <summary>Primera disponible entre clave restrictiva y secreta (API key servidor).</summary>
    public static string? StripeServerApiKey() => StripeRestrictedKey() ?? StripeSecretKey();

    public static bool EnvTruthy(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? "").Trim() is { Length: > 0 } v &&
        (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>True cuando el entorno pide omitir crear/confirmar PaymentIntents.</summary>
    public static bool SkipStripePaymentIntentCreate() =>
        EnvTruthy("VIBETRADE_SKIP_PAYMENT_INTENTS") || EnvTruthy("STRIPE_SKIP_PAYMENT_INTENTS");
}
