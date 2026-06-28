using MediatR;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionsMediator.AcceptPending;

public sealed record AcceptPendingCommand(TramoSellerSheetAction Action) : IRequest<int?>;
