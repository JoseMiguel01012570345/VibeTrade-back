using MediatR;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteSheets.ComputePath;

public sealed class ComputePathHandler(RouteSheetsChatServiceCore core)
    : IRequestHandler<ComputePathCommand, RouteSheetPayload?>
{
    public Task<RouteSheetPayload?> Handle(ComputePathCommand request, CancellationToken cancellationToken) =>
        core.ComputeRoutePathAsync(request.Payload, cancellationToken);
}
