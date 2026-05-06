using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Features.Payments;

/// <summary>Pagos de acuerdos: cálculo de checkout, estado de pagos y ejecución de cobros.</summary>
public interface IAgreementPaymentService
{
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
