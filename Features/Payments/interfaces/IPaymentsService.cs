namespace VibeTrade.Backend.Features.Payments.Interfaces;

/// <summary>Pagos: pasarela simulada, tarjetas demo y checkout/cobro de acuerdos en chat.</summary>
public interface IPaymentsService
{
    PaymentGatewayConfigDto GetPaymentGatewayConfig();

    Task<IReadOnlyList<SavedCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, object Problem, CreateSetupIntentResult? Data)> CreateSetupIntentAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)> CreatePaymentIntentAsync(
        string userId,
        CreatePaymentIntentBody body,
        CancellationToken cancellationToken = default);

    Task<BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRoutePathIds,
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
        string paymentMethodId,
        string? idempotencyKey,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRoutePathIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default);
}
