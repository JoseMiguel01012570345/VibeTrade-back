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

        var targetConfirmed = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == target
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (!targetConfirmed)
            return new CarrierOwnershipCedeResultDto(false, "target_not_carrier",
                "El otro transportista debe estar confirmado en este tramo.");

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
        return new CarrierOwnershipCedeResultDto(true, null, null);
    }
}
