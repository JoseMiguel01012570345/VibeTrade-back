using MediatR;
using VibeTrade.Backend.Features.Payments.PaymentsMediator.ConfirmPayment;
using VibeTrade.Backend.Features.Payments.PaymentsMediator.CreateCheckout;
using VibeTrade.Backend.Features.Payments.Dtos;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments;

public sealed class PaymentsService(IMediator mediator, PaymentsServiceCore core)
    : IPaymentsService,
        IAgreementPaymentService
{
    public PaymentGatewayConfigDto GetPaymentGatewayConfig() => core.GetPaymentGatewayConfig();

    public Task<IReadOnlyList<SavedCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        core.ListCardPaymentMethodsAsync(userId, cancellationToken);

    public Task<(bool Ok, object Problem, CreateSetupIntentResult? Data)> CreateSetupIntentAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        core.CreateSetupIntentAsync(userId, cancellationToken);

    public Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)> CreatePaymentIntentAsync(
        string userId,
        CreatePaymentIntentBody body,
        CancellationToken cancellationToken = default) =>
        core.CreatePaymentIntentAsync(userId, body, cancellationToken);

    public Task<BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRoutePathIds,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new CreateCheckoutQuery(
                buyerUserId,
                threadId,
                agreementId,
                selectedServicePayments,
                selectedRoutePathIds),
            cancellationToken);

    public Task<IReadOnlyList<AgreementPaymentStatusDto>> ListPaymentStatusesAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default) =>
        core.ListPaymentStatusesAsync(buyerUserId, threadId, agreementId, cancellationToken);

    public Task<AgreementExecutePaymentResultDto?> ExecuteCurrencyPaymentAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        string currencyLower,
        string PaymentMethodId,
        string? idempotencyKey,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRoutePathIds,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new ConfirmPaymentCommand(
                buyerUserId,
                threadId,
                agreementId,
                currencyLower,
                PaymentMethodId,
                idempotencyKey,
                selectedServicePayments,
                selectedRoutePathIds),
            cancellationToken);
}
