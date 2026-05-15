using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.RouteTramoSubscriptions;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Notifications;

public sealed class RouteTramoSubscriptionNotificationService(
    AppDbContext db,
    INotificationService notifications,
    IBroadcastingService broadcasting,
    IChatThreadSystemMessageService threadSystemMessages)
    : IRouteTramoSubscriptionNotificationService
{
    private static readonly JsonSerializerOptions AcceptMetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>JSON de meta para notificación <c>route_tramo_subscribe_accepted</c> y UI.</summary>
    public static string? BuildAcceptMetaJson(
        string routeSheetId,
        string carrierUserId,
        IReadOnlyList<(string StopId, string? StoreServiceId)> stops)
    {
        if (stops.Count == 0)
            return null;
        var rs = (routeSheetId ?? "").Trim();
        var cu = (carrierUserId ?? "").Trim();
        if (rs.Length < 1 || cu.Length < 2)
            return null;
        var stopObjs = stops
            .Select(t => new
            {
                stopId = (t.StopId ?? "").Trim(),
                storeServiceId = string.IsNullOrWhiteSpace(t.StoreServiceId) ? null : t.StoreServiceId.Trim(),
            })
            .Where(x => x.stopId.Length > 0)
            .ToList();
        if (stopObjs.Count == 0)
            return null;
        var payload = new
        {
            routeSheetId = rs,
            carrierUserId = cu,
            stops = stopObjs,
        };
        return JsonSerializer.Serialize(payload, AcceptMetaJsonOptions);
    }

    public Task NotifyLegHandoffsAfterCarrierConfirmedAsync(
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        IReadOnlyList<string> confirmedStopIds,
        CancellationToken cancellationToken = default)
        => RouteLegHandoffNotifications.NotifyAfterCarrierConfirmedAsync(
            db,
            notifications,
            threadId,
            routeSheetId,
            payload,
            confirmedStopIds,
            cancellationToken);

    public async Task NotifyTramoSubscriptionAcceptedAndBroadcastAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs accepted,
        string threadId,
        string routeSheetId,
        string broadcastChange,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default)
    {
        await notifications.NotifyRouteTramoSubscriptionAcceptedAsync(accepted, cancellationToken);
        var emergentPubId = await EmergentPublicationIdForSheetAsync(threadId, routeSheetId, cancellationToken);
        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                threadId,
                routeSheetId,
                broadcastChange,
                broadcastActorUserId,
                emergentPubId),
            cancellationToken);
    }

    public async Task NotifyTramoSubscriptionRejectedAndBroadcastAsync(
        RouteTramoSubscriptionRejectedNotificationArgs rejected,
        string threadId,
        string routeSheetId,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default)
    {
        await notifications.NotifyRouteTramoSubscriptionRejectedAsync(rejected, cancellationToken);
        var routeOfferId = rejected.RouteOfferId;
        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                threadId,
                routeSheetId,
                "reject",
                broadcastActorUserId,
                routeOfferId),
            cancellationToken);
    }

    public Task NotifySellerTrustPenaltyAfterConfirmedExpelAsync(
        SellerExpelContext ctx,
        int balanceAfter,
        CancellationToken cancellationToken = default)
    {
        var unit = RouteSheetEditAckComputation.StoreTrustPenaltyOnSellerExpelConfirmedCarrier;
        var deltaPenalty = -unit * ctx.ConfirmedStopsWithdrawnCount;
        var previewPenalty = ctx.ConfirmedStopsWithdrawnCount <= 1
            ? "Expulsaste a un transportista confirmado; se aplicó un ajuste de confianza a tu tienda (demo)."
            : $"Expulsaste a un transportista confirmado ({ctx.ConfirmedStopsWithdrawnCount} tramos); se aplicaron varios ajustes de confianza a tu tienda (demo).";
        return notifications.NotifySellerStoreTrustPenaltyAsync(
            new SellerStoreTrustPenaltyNotificationArgs(
                ctx.SellerUserId,
                ctx.ThreadId,
                (ctx.Thread.OfferId ?? "").Trim(),
                deltaPenalty,
                balanceAfter,
                previewPenalty),
            cancellationToken);
    }

    public async Task PublishSellerExpelledNotificationsAsync(
        SellerExpelContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (ctx.DistinctRouteSheetIds.Count == 0)
            return;

        var tid = ctx.ThreadId;
        var sid = ctx.SellerUserId;
        var carrierId = ctx.CarrierUserId;
        var reasonTrim = ctx.ReasonTrim;

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ctx.Thread.StoreId, cancellationToken);
        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        var (sellerLabel, sellerTrust) = SubscriptionsUtils.SellerLabelAndTrust(store, actorAcc);
        var carrierName = (await db.UserAccounts.AsNoTracking()
            .Where(x => x.Id == carrierId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken))?.Trim() ?? "Transportista";

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == ctx.DistinctRouteSheetIds[0]
                    && x.RetractedAtUtc == null,
                cancellationToken);
        var routeOfferId = string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();

        var preview = ctx.CarrierFullyRemovedFromThread
            ? $"La tienda te retiró de esta operación. Motivo: {reasonTrim}"
            : $"La tienda te retiró de un tramo de esta operación. Motivo: {reasonTrim}";

        await notifications.NotifyRouteTramoSellerExpelledAsync(
            new RouteTramoSellerExpelledNotificationArgs(
                carrierId,
                tid,
                preview,
                sellerLabel,
                sellerTrust,
                sid,
                routeOfferId,
                reasonTrim),
            cancellationToken);

        var sys = ctx.ExpelSingleTramo && !ctx.CarrierFullyRemovedFromThread
            ? $"{sellerLabel} retiró a {carrierName} de un tramo de la oferta de ruta. Motivo: {reasonTrim}."
            : $"{sellerLabel} retiró a {carrierName} de la oferta de ruta. Motivo: {reasonTrim}.";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, sys, cancellationToken);

        foreach (var rsid in ctx.DistinctRouteSheetIds)
        {
            var emergentPubId = await EmergentPublicationIdForSheetAsync(tid, rsid, cancellationToken);
            await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "withdraw", sid, emergentPubId),
                cancellationToken);
        }
    }

    public async Task PostCarrierWithdrawSystemNoticeAndBroadcastsAsync(
        string threadId,
        string automatedSystemNoticeText,
        string broadcastActorUserId,
        IReadOnlyList<string> distinctRouteSheetIds,
        CancellationToken cancellationToken = default)
    {
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(threadId, automatedSystemNoticeText, cancellationToken);
        foreach (var rsid in distinctRouteSheetIds)
        {
            var emergentPubId = await EmergentPublicationIdForSheetAsync(threadId, rsid, cancellationToken);
            await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(threadId, rsid, "withdraw", broadcastActorUserId, emergentPubId),
                cancellationToken);
        }
    }

    public Task NotifyPreselAcceptAndBroadcastAsync(
        RouteTramoSubscriptionAcceptedNotificationArgs accepted,
        string threadId,
        string routeSheetId,
        string broadcastActorUserId,
        CancellationToken cancellationToken = default)
        => NotifyTramoSubscriptionAcceptedAndBroadcastAsync(
            accepted,
            threadId,
            routeSheetId,
            "accept",
            broadcastActorUserId,
            cancellationToken);

    public async Task PublishPreselCarrierDeclinedAsync(
        bool sendBroadcast,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        RouteSheetPreselDeclinedByCarrierNotificationArgs declined,
        CancellationToken cancellationToken = default)
    {
        if (sendBroadcast)
        {
            var emergentPubId = await EmergentPublicationIdForSheetAsync(threadId, routeSheetId, cancellationToken);
            await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(
                    threadId,
                    routeSheetId,
                    "presel_decline",
                    carrierUserId.Trim(),
                    emergentPubId),
                cancellationToken);
        }

        await notifications.NotifyRouteSheetPreselDeclinedByCarrierAsync(declined, cancellationToken);
    }

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
}
