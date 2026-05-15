using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Notifications;

public sealed class RouteSheetThreadNotificationService(
    AppDbContext db,
    INotificationService notifications,
    IBroadcastingService broadcasting,
    IChatThreadSystemMessageService threadSystemMessages)
    : IRouteSheetThreadNotificationService
{
    public async Task PostRouteSheetUpsertEditSystemNoticeAsync(
        string userId,
        string threadId,
        RouteSheetPayload? oldSnapshot,
        RouteSheetPayload persisted,
        RouteSheetEditAckPayload? nextAck,
        HashSet<string>? affectedForNotice,
        IReadOnlyList<RouteTramoSubscriptionRow>? confirmedRowsForNotice,
        CancellationToken cancellationToken = default)
    {
        string notice;
        if (nextAck is not null
            && affectedForNotice is not null
            && affectedForNotice.Count > 0
            && confirmedRowsForNotice is not null)
        {
            var nameIds = affectedForNotice.ToList();
            var accounts = await db.UserAccounts.AsNoTracking()
                .Where(x => nameIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName ?? "", cancellationToken);
            notice = RouteSheetEditAckNoticeComposer.BuildEditNoticeText(
                persisted.Titulo,
                persisted,
                affectedForNotice,
                confirmedRowsForNotice.ToList(),
                accounts);
        }
        else
            notice = BuildRouteSheetEditedNoticeText(persisted);

        if (oldSnapshot is not null
            && confirmedRowsForNotice is { Count: > 0 }
            && RouteSheetEditAckNoticeComposer.TryBuildTramoRenumberSystemNotice(
                oldSnapshot,
                persisted,
                confirmedRowsForNotice,
                out var renumberNotice)
            && !string.IsNullOrWhiteSpace(renumberNotice))
            notice += "\n\n" + renumberNotice.Trim();

        await threadSystemMessages.PostSystemThreadNoticeAsync(userId.Trim(), threadId, notice, cancellationToken);
    }

    public async Task BroadcastRouteSheetEditPendingAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var emergentId = await EmergentPublicationIdForSheetAsync(threadId, routeSheetId, cancellationToken);
        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                threadId,
                routeSheetId,
                "sheet_edit_pending",
                userId.Trim(),
                emergentId),
            cancellationToken);
    }

    public async Task NotifyAfterRouteSheetDeletedAsync(
        string userId,
        string threadId,
        string routeSheetId,
        string? sellerUserId,
        string? offerId,
        string? sheetRawTitle,
        int nConfirmedCarriers,
        int subscribedLegsCount,
        int? storeTrustBalanceAfterDelete,
        int? storeTrustDeltaDelete,
        string? emergentPublicationId,
        CancellationToken cancellationToken = default)
    {
        if (storeTrustBalanceAfterDelete is int balDel && storeTrustDeltaDelete is int dDel)
        {
            var sellerNotify = (sellerUserId ?? "").Trim();
            if (sellerNotify.Length >= 2)
            {
                var previewDel =
                    $"Eliminaste una hoja con {nConfirmedCarriers} transportista(s) confirmado(s); se aplicó un ajuste de confianza a tu tienda ({dDel} pts, demo).";
                await notifications.NotifySellerStoreTrustPenaltyAsync(
                    new SellerStoreTrustPenaltyNotificationArgs(
                        sellerNotify,
                        threadId,
                        (offerId ?? "").Trim(),
                        dDel,
                        balDel,
                        previewDel),
                    cancellationToken);
            }
        }

        var title = (sheetRawTitle ?? "").Trim();
        if (title.Length > 120)
            title = title[..120] + "…";
        var sys = title.Length > 0 ? $"Se eliminó la hoja de ruta «{title}»." : "Se eliminó una hoja de ruta.";
        if (subscribedLegsCount > 0)
            sys += " Los transportistas con tramo en la oferta salieron del chat.";
        if (nConfirmedCarriers > 0)
            sys += $" A la tienda se aplicó un ajuste de confianza por cada transportista confirmado ({nConfirmedCarriers}× demo).";
        await threadSystemMessages.PostSystemThreadNoticeAsync(userId.Trim(), threadId, sys, cancellationToken);

        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                threadId,
                routeSheetId,
                "sheet_deleted",
                userId.Trim(),
                emergentPublicationId),
            cancellationToken);
    }

    public Task NotifySellerStoreTrustPenaltyAfterSheetEditRejectAsync(
        string sellerUserId,
        string threadId,
        string offerId,
        int balanceAfter,
        CancellationToken cancellationToken = default)
    {
        var dR = -RouteSheetEditAckComputation.StoreTrustPenaltyOnCarrierRejectSheetEdit;
        var previewR =
            "Un transportista confirmado rechazó los cambios en la hoja de ruta; se aplicó un ajuste de confianza a tu tienda (demo).";
        return notifications.NotifySellerStoreTrustPenaltyAsync(
            new SellerStoreTrustPenaltyNotificationArgs(
                sellerUserId.Trim(),
                threadId,
                offerId.Trim(),
                dR,
                balanceAfter,
                previewR),
            cancellationToken);
    }

    public Task PostAutomatedSheetEditCarrierResponseNoticeAsync(
        string threadId,
        bool accepted,
        string carrierName,
        string sheetTitle,
        CancellationToken cancellationToken = default)
    {
        var text = accepted
            ? SheetEditAcceptNotice(carrierName, sheetTitle)
            : SheetEditRejectNotice(carrierName, sheetTitle);
        return threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(threadId, text, cancellationToken);
    }

    public async Task BroadcastRouteTramoSubscriptionsSheetEditCarrierResponseAsync(
        string threadId,
        string routeSheetId,
        bool accepted,
        string carrierUserId,
        CancellationToken cancellationToken = default)
    {
        var emergentId = await EmergentPublicationIdForSheetAsync(threadId, routeSheetId, cancellationToken);
        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                threadId,
                routeSheetId,
                accepted ? "sheet_edit_accept" : "sheet_edit_reject",
                carrierUserId.Trim(),
                emergentId),
            cancellationToken);
    }

    public Task NotifyRouteSheetPreselectedTransportistaAsync(
        RouteSheetPreselectedTransportistaNotificationArgs request,
        CancellationToken cancellationToken = default)
        => notifications.NotifyRouteSheetPreselectedTransportistaAsync(request, cancellationToken);

    private async Task<string?> EmergentPublicationIdForSheetAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ThreadId == threadId && e.RouteSheetId == routeSheetId && e.RetractedAtUtc == null,
                cancellationToken);
        var id = (em?.Id ?? "").Trim();
        return id.Length > 0 ? id : null;
    }

    private static string BuildRouteSheetEditedNoticeText(RouteSheetPayload payload)
    {
        var t = (payload.Titulo ?? "").Trim();
        if (t.Length > 120)
            t = t[..120] + "…";
        return t.Length > 0
            ? $"Se actualizó la hoja de ruta «{t}»."
            : "Se actualizó la hoja de ruta.";
    }

    private static string SheetEditAcceptNotice(string carrierName, string sheetTitle) =>
        sheetTitle.Length > 0
            ? $"{carrierName} aceptó los cambios en la hoja de ruta «{sheetTitle}»."
            : $"{carrierName} aceptó los cambios en la hoja de ruta.";

    private static string SheetEditRejectNotice(string carrierName, string sheetTitle)
    {
        const string tail =
            " Sus tramos quedan libres en la oferta pública; salió del chat. A la tienda se aplicó un ajuste de confianza por la edición no aceptada.";
        return sheetTitle.Length > 0
            ? $"{carrierName} rechazó los cambios en «{sheetTitle}».{tail}"
            : $"{carrierName} rechazó los cambios en la hoja.{tail}";
    }
}
