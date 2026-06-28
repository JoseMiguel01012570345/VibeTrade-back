using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets.RouteSheetsMediator.CreateSheet;

public sealed class CreateSheetHandler(RouteSheetsChatServiceCore core)
    : IRequestHandler<CreateSheetCommand, RouteSheetMutationResult>
{
    public Task<RouteSheetMutationResult> Handle(
        CreateSheetCommand request,
        CancellationToken cancellationToken) =>
        core.UpsertAsync(
            request.UserId,
            request.ThreadId,
            request.RouteSheetId,
            request.Payload,
            cancellationToken);
}
