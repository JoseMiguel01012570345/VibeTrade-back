using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierOwnershipService(
    AppDbContext db,
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    INotificationService notifications,
    IBroadcastingService broadcasting) : ICarrierOwnershipService
{
    public async Task<CarrierOwnershipCedeResultDto?> CedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default)
    {
        var uid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var actorConfirmed = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (!actorConfirmed)
            return new CarrierOwnershipCedeResultDto(false, "not_carrier", "No sos transportista confirmado en este tramo.");

        var cedeOwnership = await GetCedeOwnershipAsync(uid, tid, rsid, sid, cancellationToken);
        if (cedeOwnership is not null && cedeOwnership.Ok)
            return new CarrierOwnershipCedeResultDto(false, "already_cede", "Ya has cedido la titularidad del paquete en este tramo.");

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var ordered = RouteLegOwnershipChain.OrderedStopIds(sheetRow?.Payload);
        var idx = RouteLegOwnershipChain.StopIndex(ordered, sid);
        var nextStopId = idx + 1 < ordered.Count ? ordered[idx + 1] : null;
        var now = DateTimeOffset.UtcNow;

        if (nextStopId is null)
        {
            db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
            {
                Id = "coe_" + Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                RouteSheetId = rsid,
                RouteStopId = sid,
                CarrierUserId = uid,
                Action = CarrierOwnershipActions.Released,
                AtUtc = now,
                Reason = "end_of_route",
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); 
            return new CarrierOwnershipCedeResultDto(true, "end_of_route", "");
        }

        var target = await ResolveAutomaticCedeTargetAsync(tid, rsid, sid, uid, ordered, cancellationToken)
            .ConfigureAwait(false);
        if (target is null || target.Length < 2)
            return new CarrierOwnershipCedeResultDto(
                false,
                "target_unresolved",
                "No se pudo determinar automáticamente el destino. Indica un transportista confirmado en este tramo o en el siguiente.");
        
        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
            return new CarrierOwnershipCedeResultDto(false, "delivery_missing", "No hay estado de entrega para este tramo.");

        if (!string.Equals(delivery.CurrentOwnerUserId, uid, StringComparison.Ordinal))
            return new CarrierOwnershipCedeResultDto(false, "not_owner", "No tienes el paquete en este tramo.");

        var nextDelivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == nextStopId && x.State == RouteStopDeliveryStates.AwaitingCarrierForHandoff && x.CurrentOwnerUserId == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (nextDelivery is null)
            return new CarrierOwnershipCedeResultDto(false, "next_delivery_missing", "No hay estado de entrega para el tramo siguiente.");

        db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
        {
            Id = "coe_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = sid,
            CarrierUserId = uid,
            Action = CarrierOwnershipActions.Released,
            AtUtc = now,
            Reason = "carrier_cede",
        });
        db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
        {
            Id = "coe_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = nextStopId,
            CarrierUserId = target,
            Action = CarrierOwnershipActions.Granted,
            AtUtc = now,
            Reason = "carrier_cede",
        });

        nextDelivery.CurrentOwnerUserId = target;
        nextDelivery.OwnershipGrantedAtUtc = now;
        nextDelivery.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "ownership_cede", uid),
                cancellationToken)
            .ConfigureAwait(false);

        var tramoTxt = idx is > 0 ? $"tramo {idx}" : "este tramo";
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
                tid,
                $"Se cedió la titularidad del paquete ({tramoTxt}). El nuevo titular recibió un aviso en la app.",
                cancellationToken)
            .ConfigureAwait(false);

        var preview = idx is > 0
            ? $"Tienes la titularidad del paquete en el tramo {idx}. Revisa Rutas en el chat para coordinar el handoff."
            : "Tienes la titularidad del paquete en este tramo. Revisa Rutas en el chat para coordinar el handoff.";
        await notifications.NotifyRouteOwnershipGrantedAsync(
                new RouteOwnershipGrantedNotificationArgs(target, tid, rsid, aid, sid, preview),
                cancellationToken)
            .ConfigureAwait(false);

        return new CarrierOwnershipCedeResultDto(true, null, null);
    }

    public async Task<CarrierOwnershipCedeResultDto?> GetCedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default)
    {
        var uid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || sid.Length < 1)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var releasedOperational = await db.CarrierOwnershipEvents.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid
                    && x.CarrierUserId == uid
                    && x.Action == CarrierOwnershipActions.Released
                    && (x.Reason == "carrier_cede" || x.Reason == "end_of_route"),
                cancellationToken)
            .ConfigureAwait(false);

        return releasedOperational ? new CarrierOwnershipCedeResultDto(true, null, null) : null;
    }

    /// <summary>
    /// Destino por defecto: primer transportista confirmado en un tramo posterior cuyo usuario sea distinto del actor
    /// (salta tramos consecutivos del mismo carrier). Si no hay, otro confirmado en el mismo tramo.
    /// </summary>
    private async Task<string?> ResolveAutomaticCedeTargetAsync(
        string threadId,
        string routeSheetId,
        string routeStopId,
        string actorUserId,
        IReadOnlyList<string> ordered,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        var actor = (actorUserId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || sid.Length < 1 || actor.Length < 2)
            return null;

        var idx = RouteLegOwnershipChain.StopIndex(ordered, sid);
        if (idx < 0)
            return null;

        for (var j = idx + 1; j < ordered.Count; j++)
        {
            var nextSid = (ordered[j] ?? "").Trim();
            if (nextSid.Length == 0)
                continue;
            var nc = await db.RouteTramoSubscriptions.AsNoTracking()
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == nextSid
                    && x.Status == "confirmed")
                .Select(x => x.CarrierUserId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var nextUid = (nc ?? "").Trim();
            if (nextUid.Length >= 2)
                return nextUid;
        }
        return null;
    }
}
