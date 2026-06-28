namespace VibeTrade.Backend.Features.Payments;

public sealed record HandleStripeWebhookResult(
    bool Ok,
    string Outcome,
    string? PaymentIntentId = null,
    string? EventType = null)
{
    public static HandleStripeWebhookResult NotConfigured() =>
        new(false, "not_configured");

    public static HandleStripeWebhookResult InvalidPayload() =>
        new(false, "invalid_payload");

    public static HandleStripeWebhookResult InvalidSignature() =>
        new(false, "invalid_signature");

    public static HandleStripeWebhookResult Ignored(string eventType) =>
        new(true, "ignored", EventType: eventType);

    public static HandleStripeWebhookResult PaymentNotFound(string paymentIntentId) =>
        new(false, "payment_not_found", paymentIntentId);

    public static HandleStripeWebhookResult AlreadyProcessed(string paymentIntentId) =>
        new(true, "already_processed", paymentIntentId);

    public static HandleStripeWebhookResult Processed(string paymentIntentId) =>
        new(true, "processed", paymentIntentId);
}
