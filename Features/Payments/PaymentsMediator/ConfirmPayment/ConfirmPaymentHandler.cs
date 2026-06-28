using MediatR;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.PaymentsMediator.ConfirmPayment;

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
            request.PaymentMethodId,
            request.IdempotencyKey,
            request.SelectedServicePayments,
            request.SelectedRoutePathIds,
            request.SelectedMerchandiseLineIds,
            cancellationToken);
}
