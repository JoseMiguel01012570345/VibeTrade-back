using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets.RouteSheetsMediator.CreateSheet;

public sealed record CreateSheetCommand(
    string UserId,
    string ThreadId,
    string RouteSheetId,
    RouteSheetPayload Payload) : IRequest<RouteSheetMutationResult>;
