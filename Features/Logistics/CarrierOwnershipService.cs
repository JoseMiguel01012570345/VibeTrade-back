using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierOwnershipService(AppDbContext db, IChatService chat) : ICarrierOwnershipService
{
    public async Task<CarrierOwnershipCedeResultDto?> CedeOwnershipAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        string targetCarrierUserId,
        CancellationToken cancellationToken = default)
    {
        var uid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        var target = (targetCarrierUserId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1 || target.Length < 2)
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

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var ordered = RouteLegOwnershipChain.OrderedStopIds(sheetRow?.Payload);
        var nextStopId = RouteLegOwnershipChain.NextStopId(ordered, sid);
        var ordenStop = (sheetRow?.Payload?.Paradas ?? [])
            .FirstOrDefault(pp =>
                string.Equals((pp.Id ?? "").Trim(), sid, StringComparison.Ordinal))
            ?.Orden;

        var actorThisUid = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.StopId == sid
                && x.CarrierUserId == uid
                && x.Status == "confirmed")
            .Select(x => x.CarrierUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var actorUidNorm = (actorThisUid ?? "").Trim();

        string? nextCarrierUid = null;
        if (nextStopId is not null)
        {
            var nc = await db.RouteTramoSubscriptions.AsNoTracking()
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == nextStopId
                    && x.Status == "confirmed")
                .Select(x => x.CarrierUserId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            nextCarrierUid = (nc ?? "").Trim();
        }

        if (nextStopId is not null
            && actorUidNorm.Length >= 2
            && nextCarrierUid.Length >= 2
            && string.Equals(actorUidNorm, nextCarrierUid, StringComparison.Ordinal))
            return new CarrierOwnershipCedeResultDto(false, "cede_not_applicable",
                "No aplica ceder aquí: el siguiente tramo es del mismo transportista.");

        var targetOnThisStop = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == target
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        var targetOnNextStop = nextStopId is not null
            && await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                    x =>
                        x.ThreadId == tid
                        && x.RouteSheetId == rsid
                        && x.StopId == nextStopId
                        && x.CarrierUserId == target
                        && x.Status == "confirmed",
                    cancellationToken)
                .ConfigureAwait(false);

        if (!targetOnThisStop && !targetOnNextStop)
            return new CarrierOwnershipCedeResultDto(false, "target_not_carrier",
                "El destino debe ser transportista confirmado en este tramo o en el tramo siguiente.");

        if (nextStopId is null && !targetOnThisStop)
            return new CarrierOwnershipCedeResultDto(false, "cede_not_applicable",
                "En el último tramo solo podés ceder a otro transportista confirmado en este mismo tramo.");

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
            return new CarrierOwnershipCedeResultDto(false, "not_owner", "No tenés el paquete en este tramo.");

        if (string.Equals(uid, target, StringComparison.Ordinal))
            return new CarrierOwnershipCedeResultDto(false, "noop", "Ya sos el titular.");

        var now = DateTimeOffset.UtcNow;

        if (!targetOnThisStop && targetOnNextStop && nextStopId is not null)
        {
            await TryMirrorConfirmedSubscriptionOntoStopAsync(
                    tid,
                    rsid,
                    sid,
                    nextStopId,
                    target,
                    ordenStop is > 0 ? ordenStop : null,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

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
            RouteStopId = sid,
            CarrierUserId = target,
            Action = CarrierOwnershipActions.Granted,
            AtUtc = now,
            Reason = "carrier_cede",
        });

        delivery.CurrentOwnerUserId = target;
        delivery.OwnershipGrantedAtUtc = now;
        delivery.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "ownership_cede", uid),
                cancellationToken)
            .ConfigureAwait(false);

        var tramoTxt = ordenStop is > 0 ? $"tramo {ordenStop}" : "este tramo";
        await chat.PostAutomatedSystemThreadNoticeAsync(
                tid,
                $"Se cedió la titularidad del paquete ({tramoTxt}). El nuevo titular recibió un aviso en la app.",
                cancellationToken)
            .ConfigureAwait(false);

        var preview = ordenStop is > 0
            ? $"Tenés la titularidad del paquete en el tramo {ordenStop}. Revisá Rutas en el chat para coordinar el handoff."
            : "Tenés la titularidad del paquete en este tramo. Revisá Rutas en el chat para coordinar el handoff.";
        await chat.NotifyRouteOwnershipGrantedAsync(
                new RouteOwnershipGrantedNotificationArgs(target, tid, rsid, aid, sid, preview),
                cancellationToken)
            .ConfigureAwait(false);

        return new CarrierOwnershipCedeResultDto(true, null, null);
    }

    /// <summary>
    /// Si el receptor solo estaba confirmado en el tramo siguiente, replica la suscripción en el tramo actual
    /// para que el titular coincida con «confirmado en este tramo» en cliente y reglas posteriores.
    /// </summary>
    private async Task<bool> TryMirrorConfirmedSubscriptionOntoStopAsync(
        string threadId,
        string routeSheetId,
        string ontoStopId,
        string fromNextStopId,
        string carrierUserId,
        int? ontoStopOrden,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var onto = (ontoStopId ?? "").Trim();
        var fromNext = (fromNextStopId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || onto.Length < 1 || fromNext.Length < 1 || cid.Length < 2)
            return false;

        var existsHere = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == onto
                    && x.CarrierUserId == cid
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (existsHere)
            return false;

        var template = await db.RouteTramoSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == fromNext
                    && x.CarrierUserId == cid
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
            return false;

        var orden = ontoStopOrden is > 0 ? ontoStopOrden!.Value : template.StopOrden;
        db.RouteTramoSubscriptions.Add(new RouteTramoSubscriptionRow
        {
            Id = "rts_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            StopId = onto,
            StopOrden = orden,
            CarrierUserId = cid,
            CarrierPhoneSnapshot = template.CarrierPhoneSnapshot,
            StoreServiceId = template.StoreServiceId,
            TransportServiceLabel = template.TransportServiceLabel ?? "",
            Status = "confirmed",
            StopContentFingerprint = template.StopContentFingerprint,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        return true;
    }
}
