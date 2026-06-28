using MediatR;
using VibeTrade.Backend.Features.RouteSheets.EditSheet;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets;

public sealed class RouteSheetsChatService(IMediator mediator, RouteSheetsChatServiceCore core)
    : IRouteSheetChatService
{
    public Task<IReadOnlyList<RouteSheetPayload>?> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        core.ListForThreadAsync(userId, threadId, cancellationToken);

    public Task<RouteSheetPayload?> GetPreselPreviewForCarrierAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        core.GetPreselPreviewForCarrierAsync(carrierUserId, threadId, routeSheetId, cancellationToken);

    public Task<RouteSheetMutationResult> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new EditSheetCommand(userId, threadId, routeSheetId, payload), cancellationToken);

    public Task<RouteSheetMutationResult> DeleteAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        core.DeleteAsync(userId, threadId, routeSheetId, cancellationToken);

    public Task<bool> AutoArchiveOnRouteCompletedAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string acceptedRouteStopId,
        CancellationToken cancellationToken = default) =>
        core.AutoArchiveOnRouteCompletedAsync(
            actorUserId, threadId, routeSheetId, acceptedRouteStopId, cancellationToken);

    public Task<(RouteSheetPayload? Payload, RouteSheetMutationResult? Error)> DuplicateAsync(
        string userId,
        string threadId,
        string sourceRouteSheetId,
        CancellationToken cancellationToken = default) =>
        core.DuplicateAsync(userId, threadId, sourceRouteSheetId, cancellationToken);

    public Task<bool> RouteSheetIsLockedByPaidAgreementAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        core.RouteSheetIsLockedByPaidAgreementAsync(threadId, routeSheetId, cancellationToken);

    public Task<HashSet<string>> LoadConfirmedRouteStopIdsAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default) =>
        core.LoadConfirmedRouteStopIdsAsync(threadId, routeSheetId, cancellationToken);

    public Task<bool> CarrierRespondToSheetEditAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        bool accept,
        CancellationToken cancellationToken = default) =>
        core.CarrierRespondToSheetEditAsync(carrierUserId, threadId, routeSheetId, accept, cancellationToken);

    public Task<int> NotifyPreselectedTransportistasAsync(
        string editorUserId,
        string threadId,
        string routeSheetId,
        IReadOnlyList<RouteSheetPreselectedInvite> invites,
        CancellationToken cancellationToken = default) =>
        core.NotifyPreselectedTransportistasAsync(
            editorUserId, threadId, routeSheetId, invites, cancellationToken);
}
