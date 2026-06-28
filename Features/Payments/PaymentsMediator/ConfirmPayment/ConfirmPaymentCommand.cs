using MediatR;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.PaymentsMediator.ConfirmPayment;

public sealed record ConfirmPaymentCommand(
    string BuyerUserId,
    string ThreadId,
    string AgreementId,
    string CurrencyLower,
    string PaymentMethodId,
    string? IdempotencyKey,
    IReadOnlyList<ServicePaymentPickDto>? SelectedServicePayments,
    IReadOnlyList<string>? SelectedRoutePathIds,
    IReadOnlyList<string>? SelectedMerchandiseLineIds = null)
    : IRequest<AgreementExecutePaymentResultDto?>;
