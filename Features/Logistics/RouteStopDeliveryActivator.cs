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

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var ordered = RouteLegOwnershipChain.OrderedStopIds(sheetRow?.Payload);

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

        var agreementIds = rows
            .Select(r => (r.TradeAgreementId ?? "").Trim())
            .Where(x => x.Length >= 8)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var siblingRows = agreementIds.Count == 0
            ? []
            : await db.RouteStopDeliveries
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && agreementIds.Contains(x.TradeAgreementId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var byAgreement = siblingRows
            .GroupBy(r => (r.TradeAgreementId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.RouteStopId.Trim(), StringComparer.Ordinal),
                StringComparer.Ordinal);

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

            var aid = (row.TradeAgreementId ?? "").Trim();
            var byStop = byAgreement.TryGetValue(aid, out var dict) ? dict : new Dictionary<string, RouteStopDeliveryRow>(StringComparer.Ordinal);
            if (ordered.Count > 0
                && !RouteLegOwnershipChain.MayAssignOperationalOwner(ordered, byStop, row.RouteStopId.Trim()))
                continue;

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
