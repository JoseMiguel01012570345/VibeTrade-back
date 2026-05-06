namespace VibeTrade.Backend.Features.Payments.Interfaces;

/// <summary>Configuración Stripe y métodos de pago del usuario (tarjetas, setup intents).</summary>
public interface IStripeUserPaymentService
{
    StripeConfigDto GetStripeConfig();

    Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, object Problem, CreateSetupIntentResult? Data)> CreateSetupIntentAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
