using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets.EditSheet;

public sealed record EditSheetCommand(
    string UserId,
    string ThreadId,
    string RouteSheetId,
    RouteSheetPayload Payload) : IRequest<RouteSheetMutationResult>;
