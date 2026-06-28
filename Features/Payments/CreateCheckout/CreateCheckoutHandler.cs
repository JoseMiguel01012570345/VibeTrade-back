using MediatR;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.CreateCheckout;

public sealed class CreateCheckoutHandler(PaymentsServiceCore core)
    : IRequestHandler<CreateCheckoutQuery, BreakdownDto?>
{
    public Task<BreakdownDto?> Handle(CreateCheckoutQuery request, CancellationToken cancellationToken) =>
        core.GetCheckoutBreakdownAsync(
            request.BuyerUserId,
            request.ThreadId,
            request.AgreementId,
            request.SelectedServicePayments,
            request.SelectedRoutePathIds,
            request.SelectedMerchandiseLineIds,
            cancellationToken);
}
