namespace VibeTrade.Backend.Features.Payments.Interfaces;

/// <summary>Creación de PaymentIntents en Stripe.</summary>
public interface IStripePaymentIntentService
{
    Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)> CreatePaymentIntentAsync(
        string userId,
        CreatePaymentIntentBody body,
        CancellationToken cancellationToken = default);
}
