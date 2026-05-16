using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class SellerRouteStopDeliveryCustodyService(
    AppDbContext db,
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    INotificationService notifications,
    IBroadcastingService broadcasting) : ISellerRouteStopDeliveryCustodyService
{
    public async Task<SellerRouteStopCustodyResult> PauseForStoreCustodyAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var sid = (sellerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var stop = (routeStopId ?? "").Trim();
        var reasonTrim = (reason ?? "").Trim();
        if (sid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || stop.Length < 1)
            return new SellerRouteStopCustodyResult(false, "invalid_request", "Solicitud inválida.");
        if (reasonTrim.Length < 1)
            return new SellerRouteStopCustodyResult(false, "reason_required", "Indicá el motivo de la pausa.");
        if (reasonTrim.Length > 2000)
            reasonTrim = reasonTrim[..2000];

        var thread = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null || thread.DeletedAtUtc is not null)
            return new SellerRouteStopCustodyResult(false, "thread_not_found", "El hilo no existe.");
        if (!string.Equals(thread.SellerUserId, sid, StringComparison.Ordinal))
            return new SellerRouteStopCustodyResult(false, "forbidden", "Solo la tienda del hilo puede pausar el tramo.");
        if (!await chat.UserCanAccessThreadRowAsync(sid, thread, cancellationToken).ConfigureAwait(false))
            return new SellerRouteStopCustodyResult(false, "forbidden", "Sin acceso al hilo.");

        var agreement = await db.TradeAgreements.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == aid && x.ThreadId == tid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (agreement is null
            || !string.Equals(agreement.Status, "accepted", StringComparison.OrdinalIgnoreCase)
            || !agreement.IncludeMerchandise
            || !string.Equals((agreement.RouteSheetId ?? "").Trim(), rsid, StringComparison.Ordinal))
            return new SellerRouteStopCustodyResult(false, "agreement_mismatch", "Acuerdo no válido para esta hoja.");

        var otherIdle = await db.RouteStopDeliveries.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId != stop
                    && x.State == RouteStopDeliveryStates.IdleStoreCustody
                    && x.RefundedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (otherIdle)
            return new SellerRouteStopCustodyResult(
                false,
                "route_sheet_already_idle",
                "Ya hay otro tramo en pausa en esta hoja. Reanudalo antes de pausar otro.");

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == stop,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
            return new SellerRouteStopCustodyResult(false, "delivery_missing", "No hay estado de entrega para este tramo.");

        var prevOwner = (delivery.CurrentOwnerUserId ?? "").Trim();
        if (prevOwner.Length < 2)
            return new SellerRouteStopCustodyResult(
                false,
                "no_active_owner",
                "No hay titular del paquete en este tramo para pausar.");

        var canPause =
            string.Equals(delivery.State, RouteStopDeliveryStates.InTransit, StringComparison.OrdinalIgnoreCase)
            || prevOwner.Length >= 2;
        if (!canPause)
            return new SellerRouteStopCustodyResult(
                false,
                "invalid_state_for_pause",
                "Solo se puede pausar un tramo en tránsito (con titular asignado).");

        var now = DateTimeOffset.UtcNow;
        delivery.State = RouteStopDeliveryStates.IdleStoreCustody;
        delivery.CurrentOwnerUserId = null;
        delivery.OwnershipGrantedAtUtc = null;
        delivery.UpdatedAtUtc = now;

        db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
        {
            Id = "coe_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = stop,
            CarrierUserId = prevOwner,
            Action = CarrierOwnershipActions.Released,
            AtUtc = now,
            Reason = "store_exception_idle",
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "route_stop_idle", sid),
                cancellationToken)
            .ConfigureAwait(false);

        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
                tid,
                $"La tienda pausó el tramo (custodia tienda). Motivo: {reasonTrim}",
                cancellationToken)
            .ConfigureAwait(false);

        return new SellerRouteStopCustodyResult(true, null, null);
    }

    public async Task<SellerRouteStopCustodyResult> ResumeFromIdleAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        string targetCarrierUserId,
        CancellationToken cancellationToken = default)
    {
        var sid = (sellerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var stop = (routeStopId ?? "").Trim();
        var target = (targetCarrierUserId ?? "").Trim();
        if (sid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || stop.Length < 1 || target.Length < 2)
            return new SellerRouteStopCustodyResult(false, "invalid_request", "Solicitud inválida.");

        var thread = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null || thread.DeletedAtUtc is not null)
            return new SellerRouteStopCustodyResult(false, "thread_not_found", "El hilo no existe.");
        if (!string.Equals(thread.SellerUserId, sid, StringComparison.Ordinal))
            return new SellerRouteStopCustodyResult(false, "forbidden", "Solo la tienda del hilo puede reanudar el tramo.");
        if (!await chat.UserCanAccessThreadRowAsync(sid, thread, cancellationToken).ConfigureAwait(false))
            return new SellerRouteStopCustodyResult(false, "forbidden", "Sin acceso al hilo.");

        var agreement = await db.TradeAgreements.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == aid && x.ThreadId == tid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (agreement is null
            || !string.Equals(agreement.Status, "accepted", StringComparison.OrdinalIgnoreCase)
            || !agreement.IncludeMerchandise
            || !string.Equals((agreement.RouteSheetId ?? "").Trim(), rsid, StringComparison.Ordinal))
            return new SellerRouteStopCustodyResult(false, "agreement_mismatch", "Acuerdo no válido para esta hoja.");

        var confirmed = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == stop
                    && x.CarrierUserId == target
                    && x.Status != null
                    && x.Status.ToLower() == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (!confirmed)
            return new SellerRouteStopCustodyResult(
                false,
                "target_not_confirmed",
                "El transportista elegido no está confirmado en este tramo.");

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == stop,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
            return new SellerRouteStopCustodyResult(false, "delivery_missing", "No hay estado de entrega para este tramo.");

        if (!string.Equals(delivery.State, RouteStopDeliveryStates.IdleStoreCustody, StringComparison.OrdinalIgnoreCase))
            return new SellerRouteStopCustodyResult(
                false,
                "not_idle",
                "El tramo no está en pausa (custodia tienda).");

        if (!string.IsNullOrWhiteSpace(delivery.CurrentOwnerUserId))
            return new SellerRouteStopCustodyResult(false, "still_has_owner", "El tramo aún tiene titular asignado.");

        var now = DateTimeOffset.UtcNow;
        delivery.CurrentOwnerUserId = target;
        delivery.OwnershipGrantedAtUtc = now;
        delivery.State = RouteStopDeliveryStates.InTransit;
        delivery.UpdatedAtUtc = now;

        db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
        {
            Id = "coe_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = stop,
            CarrierUserId = target,
            Action = CarrierOwnershipActions.Granted,
            AtUtc = now,
            Reason = "seller_resume_from_idle",
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "ownership_resume_idle", sid),
                cancellationToken)
            .ConfigureAwait(false);

        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
                tid,
                "La tienda reanudó el tramo: el transportista asignado recuperó la titularidad en tránsito.",
                cancellationToken)
            .ConfigureAwait(false);

        var preview =
            "La tienda reanudó el tramo: tenés la titularidad del paquete en tránsito. Revisá Rutas en el chat.";
        await notifications.NotifyRouteOwnershipGrantedAsync(
                new RouteOwnershipGrantedNotificationArgs(target, tid, rsid, aid, stop, preview),
                cancellationToken)
            .ConfigureAwait(false);

        return new SellerRouteStopCustodyResult(true, null, null);
    }
}
