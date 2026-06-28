using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.Unsubscribe;

public sealed record UnsubscribeCommand(
    string CarrierUserId,
    string ThreadId,
    string WithdrawReason,
    string? TradeAgreementId = null) : IRequest<CarrierWithdrawFromThreadResult?>;
