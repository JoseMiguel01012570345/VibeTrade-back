using MediatR;
using VibeTrade.Backend.Features.Payments.ConfirmPayment;
using VibeTrade.Backend.Features.Payments.CreateCheckout;
using VibeTrade.Backend.Features.Payments.HandleStripeWebhook;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments;

public sealed class PaymentsService(IMediator mediator, PaymentsServiceCore core)
    : IPaymentsService,
        IStripeUserPaymentService,
        IStripePaymentIntentService,
        IAgreementPaymentService
{
    public StripeConfigDto GetStripeConfig() => core.GetStripeConfig();

    public Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
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
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new CreateCheckoutQuery(
                buyerUserId,
                threadId,
                agreementId,
                selectedServicePayments,
                selectedRoutePathIds,
                selectedMerchandiseLineIds),
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
        string paymentMethodStripeId,
        string? idempotencyKey,
        IReadOnlyList<ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRoutePathIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default) =>
        mediator.Send(
            new ConfirmPaymentCommand(
                buyerUserId,
                threadId,
                agreementId,
                currencyLower,
                paymentMethodStripeId,
                idempotencyKey,
                selectedServicePayments,
                selectedRoutePathIds,
                selectedMerchandiseLineIds),
            cancellationToken);

    public Task<HandleStripeWebhookResult> HandleStripeWebhookAsync(
        string json,
        string stripeSignature,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new HandleStripeWebhookCommand(json, stripeSignature), cancellationToken);
}
