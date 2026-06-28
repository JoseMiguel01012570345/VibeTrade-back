using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteSheets.ComputePath;

public sealed record ComputePathCommand(RouteSheetPayload Payload) : IRequest<RouteSheetPayload?>;
