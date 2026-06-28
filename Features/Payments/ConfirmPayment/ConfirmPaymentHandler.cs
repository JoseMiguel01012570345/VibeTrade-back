using MediatR;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.ConfirmPayment;

public sealed class ConfirmPaymentHandler(PaymentsServiceCore core)
    : IRequestHandler<ConfirmPaymentCommand, AgreementExecutePaymentResultDto?>
{
    public Task<AgreementExecutePaymentResultDto?> Handle(
        ConfirmPaymentCommand request,
        CancellationToken cancellationToken) =>
        core.ExecuteCurrencyPaymentAsync(
            request.BuyerUserId,
            request.ThreadId,
            request.AgreementId,
            request.CurrencyLower,
            request.PaymentMethodStripeId,
            request.IdempotencyKey,
            request.SelectedServicePayments,
            request.SelectedRoutePathIds,
            request.SelectedMerchandiseLineIds,
            cancellationToken);
}
