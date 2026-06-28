using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets.EditSheet;

public sealed class EditSheetHandler(RouteSheetsChatServiceCore core)
    : IRequestHandler<EditSheetCommand, RouteSheetMutationResult>
{
    public Task<RouteSheetMutationResult> Handle(
        EditSheetCommand request,
        CancellationToken cancellationToken) =>
        core.UpsertAsync(
            request.UserId,
            request.ThreadId,
            request.RouteSheetId,
            request.Payload,
            cancellationToken);
}
