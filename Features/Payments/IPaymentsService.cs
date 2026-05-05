using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Features.Payments;

/// <summary>Pagos: Stripe (config, tarjetas, intents) y checkout/cobro de acuerdos en chat.</summary>
public interface IPaymentsService
{
    StripeConfigDto GetStripeConfig();

    Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, object Problem, CreateSetupIntentResult? Data)> CreateSetupIntentAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)> CreatePaymentIntentAsync(
        string userId,
        CreatePaymentIntentBody body,
        CancellationToken cancellationToken = default);

    Task<PaymentCheckoutComputation.BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgreementPaymentStatusDto>> ListPaymentStatusesAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default);

    Task<AgreementExecutePaymentResultDto?> ExecuteCurrencyPaymentAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        string currencyLower,
        string paymentMethodStripeId,
        string? idempotencyKey,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default);
}
