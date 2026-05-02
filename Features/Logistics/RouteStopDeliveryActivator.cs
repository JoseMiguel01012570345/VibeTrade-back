using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
namespace VibeTrade.Backend.Features.Logistics;

public static class RouteStopDeliveryActivator
{
    public static async Task ApplyConfirmedCarriersAsync(
        AppDbContext db,
        string threadId,
        string routeSheetId,
        IReadOnlyCollection<string> confirmedStopIds,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || confirmedStopIds.Count == 0)
            return;

        var stopSet = confirmedStopIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var carrierByStop = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.Status == "confirmed"
                && stopSet.Contains(x.StopId))
            .Select(x => new { x.StopId, x.CarrierUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var map = carrierByStop
            .GroupBy(x => (x.StopId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().CarrierUserId ?? "").Trim(), StringComparer.Ordinal);

        var rows = await db.RouteStopDeliveries
                .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && stopSet.Contains(x.RouteStopId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            ;

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.RouteStopId.Trim(), out var carrier) || carrier.Length < 2)
                continue;

            var paidEnough =
                row.State == RouteStopDeliveryStates.Paid
                || row.State == RouteStopDeliveryStates.InTransit
                || row.State == RouteStopDeliveryStates.DeliveredPendingEvidence
                || row.State == RouteStopDeliveryStates.EvidenceSubmitted
                || row.State == RouteStopDeliveryStates.EvidenceAccepted
                || row.State == RouteStopDeliveryStates.EvidenceRejected;

            if (row.State == RouteStopDeliveryStates.AwaitingCarrierForHandoff ||
                (paidEnough && string.IsNullOrWhiteSpace(row.CurrentOwnerUserId)))
            {
                row.State = paidEnough ? row.State : RouteStopDeliveryStates.Paid;
                row.CurrentOwnerUserId = carrier;
                row.OwnershipGrantedAtUtc = now;
                row.UpdatedAtUtc = now;

                db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
                {
                    Id = "coe_" + Guid.NewGuid().ToString("N"),
                    ThreadId = tid,
                    RouteSheetId = rsid,
                    RouteStopId = row.RouteStopId,
                    CarrierUserId = carrier,
                    Action = CarrierOwnershipActions.Granted,
                    AtUtc = now,
                    Reason = "carrier_confirmed",
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
