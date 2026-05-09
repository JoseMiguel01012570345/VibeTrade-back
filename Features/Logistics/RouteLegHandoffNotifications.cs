using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Dtos;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Logistics;

public static class RouteLegHandoffNotifications
{
    public static async Task NotifyPaidStopsAsync(
        AppDbContext db,
        IChatService chat,
        string threadId,
        string agreementId,
        string routeSheetId,
        RouteSheetPayload payload,
        HashSet<string> paidStopIds,
        CancellationToken cancellationToken)
    {
        var orderedStopIds = (payload.Paradas ?? [])
            .OrderBy(p => p.Orden)
            .Select(p => (p.Id ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();

        var carrierByStop = await ConfirmedCarriersAsync(db, threadId, routeSheetId, cancellationToken)
            .ConfigureAwait(false);

        await NotifyForOrderedStopsAsync(
                db,
                chat,
                threadId,
                agreementId,
                routeSheetId,
                orderedStopIds,
                paidStopIds,
                carrierByStop,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task NotifyAfterCarrierConfirmedAsync(
        AppDbContext db,
        IChatService chat,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        IReadOnlyCollection<string> confirmedStopIds,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1)
            return;

        var stopSet = confirmedStopIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (stopSet.Count == 0)
            return;

        var agreements = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && stopSet.Contains(x.RouteStopId))
            .Select(x => x.TradeAgreementId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var carrierByStop = await ConfirmedCarriersAsync(db, tid, rsid, cancellationToken).ConfigureAwait(false);

        foreach (var agr in agreements)
        {
            var paid = await db.RouteStopDeliveries.AsNoTracking()
                .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.TradeAgreementId == agr)
                .Where(x =>
                    x.State != RouteStopDeliveryStates.Unpaid)
                .Select(x => x.RouteStopId.Trim())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var paidSet = paid.ToHashSet(StringComparer.Ordinal);

            var orderedStopIds = (payload.Paradas ?? [])
                .OrderBy(p => p.Orden)
                .Select(p => (p.Id ?? "").Trim())
                .Where(x => x.Length > 0)
                .ToList();

            await NotifyForOrderedStopsAsync(
                    db,
                    chat,
                    tid,
                    agr,
                    rsid,
                    orderedStopIds,
                    paidSet,
                    carrierByStop,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, string>> ConfirmedCarriersAsync(
        AppDbContext db,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var subs = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.RouteSheetId == routeSheetId
                && x.Status == "confirmed")
            .Select(x => new { x.StopId, x.CarrierUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return subs
            .GroupBy(x => (x.StopId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().CarrierUserId ?? "").Trim(), StringComparer.Ordinal);
    }

    private static async Task NotifyForOrderedStopsAsync(
        AppDbContext db,
        IChatService chat,
        string threadId,
        string agreementId,
        string routeSheetId,
        IReadOnlyList<string> orderedStopIds,
        HashSet<string> paidStopIds,
        Dictionary<string, string> carrierByStop,
        CancellationToken cancellationToken)
    {
        var deliveries = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == threadId && x.TradeAgreementId == agreementId && x.RouteSheetId == routeSheetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stateByStop = deliveries.ToDictionary(x => x.RouteStopId.Trim(), x => x, StringComparer.Ordinal);

        for (var idx = 0; idx < orderedStopIds.Count; idx++)
        {
            var stopId = orderedStopIds[idx];
            if (!paidStopIds.Contains(stopId))
                continue;
            if (!carrierByStop.TryGetValue(stopId, out var nextCarrier) || nextCarrier.Length < 2)
                continue;

            var prevId = orderedStopIds[idx - 1];
            stateByStop.TryGetValue(prevId, out var prevRow);

            var prevCarrier = (prevRow?.CurrentOwnerUserId ?? "").Trim();
            if (prevCarrier.Length >= 2 &&
                string.Equals(prevCarrier, nextCarrier, StringComparison.Ordinal))
                continue;

            var preview =
                "Hay pagos en tu hoja de ruta: podrás iniciar tu tramo cuando corresponda.";
            await chat.NotifyRouteLegHandoffReadyAsync(
                    new RouteLegHandoffReadyNotificationArgs(
                        nextCarrier,
                        threadId.Trim(),
                        routeSheetId,
                        agreementId.Trim(),
                        stopId,
                        preview),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
