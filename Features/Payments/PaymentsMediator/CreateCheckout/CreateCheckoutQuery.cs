using MediatR;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.PaymentsMediator.CreateCheckout;

public sealed record CreateCheckoutQuery(
    string BuyerUserId,
    string ThreadId,
    string AgreementId,
    IReadOnlyList<ServicePaymentPickDto>? SelectedServicePayments,
    IReadOnlyList<string>? SelectedRoutePathIds,
    IReadOnlyList<string>? SelectedMerchandiseLineIds = null) : IRequest<BreakdownDto?>;
