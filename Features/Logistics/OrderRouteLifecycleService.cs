using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Logistics.Entities;
using VibeTrade.Backend.Features.Logistics.Interfaces;

namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Implementa el ciclo de vida de los tramos de mercancía ligados al pedido. Reutiliza los
/// <see cref="RouteStopDeliveryRow"/> y eventos de titularidad existentes, re-parentados por <c>OrderId</c>.
/// </summary>
public sealed class OrderRouteLifecycleService(AppDbContext db) : IOrderRouteLifecycleService
{
    private static readonly string[] ResolvedLegStates =
    [
        RouteStopDeliveryStates.EvidenceAccepted,
        RouteStopDeliveryStates.Refunded,
    ];

    public async Task LinkRouteSheetAsync(
        string orderId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var oid = (orderId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (oid.Length == 0 || tid.Length == 0 || rsid.Length == 0)
            return;

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == oid, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return;

        var sheet = await db.ChatRouteSheets
            .FirstOrDefaultAsync(r => r.ThreadId == tid && r.RouteSheetId == rsid, cancellationToken)
            .ConfigureAwait(false);
        if (sheet is null)
            return;

        order.RouteSheetId = rsid;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;
        sheet.OrderId = oid;
        sheet.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OnOrderInTransitAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var oid = (orderId ?? "").Trim();
        if (oid.Length == 0)
            return;

        var sheet = await db.ChatRouteSheets
            .FirstOrDefaultAsync(r => r.OrderId == oid && r.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (sheet is null)
            return;

        var ordered = LogisticsUtils.OrderedStopIds(sheet.Payload);
        if (ordered.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var subs = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(s => s.ThreadId == sheet.ThreadId && s.RouteSheetId == sheet.RouteSheetId && s.Status == "confirmed")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await db.RouteStopDeliveries
            .Where(x => x.OrderId == oid && x.RouteSheetId == sheet.RouteSheetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < ordered.Count; i++)
        {
            var stopId = ordered[i];
            var confirmedCarrier = subs
                .Where(s => string.Equals((s.StopId ?? "").Trim(), stopId, StringComparison.Ordinal))
                .Select(s => (s.CarrierUserId ?? "").Trim())
                .FirstOrDefault(c => c.Length >= 2);

            var row = existing.FirstOrDefault(x => string.Equals(x.RouteStopId, stopId, StringComparison.Ordinal));
            if (row is null)
            {
                row = new RouteStopDeliveryRow
                {
                    Id = "rsd_" + Guid.NewGuid().ToString("N"),
                    ThreadId = sheet.ThreadId,
                    OrderId = oid,
                    TradeAgreementId = "",
                    RouteSheetId = sheet.RouteSheetId,
                    RouteStopId = stopId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.RouteStopDeliveries.Add(row);
            }

            // Transportista confirmado en mercancía: siempre pagado (sin paso de pago manual).
            if (row.State is RouteStopDeliveryStates.Unpaid or RouteStopDeliveryStates.Paid)
                row.State = RouteStopDeliveryStates.Paid;
            row.UpdatedAtUtc = now;

            // La titularidad se activa en el primer tramo con transportista confirmado.
            if (i == 0 && confirmedCarrier is { Length: >= 2 } && string.IsNullOrWhiteSpace(row.CurrentOwnerUserId))
            {
                row.State = RouteStopDeliveryStates.InTransit;
                row.CurrentOwnerUserId = confirmedCarrier;
                row.OwnershipGrantedAtUtc = now;
                db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
                {
                    Id = "coe_" + Guid.NewGuid().ToString("N"),
                    ThreadId = sheet.ThreadId,
                    RouteSheetId = sheet.RouteSheetId,
                    RouteStopId = stopId,
                    CarrierUserId = confirmedCarrier,
                    Action = CarrierOwnershipActions.Granted,
                    AtUtc = now,
                    Reason = "order_in_transit",
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AllMerchandiseLegsResolvedAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var oid = (orderId ?? "").Trim();
        if (oid.Length == 0)
            return true;

        var legs = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.OrderId == oid)
            .Select(x => x.State)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (legs.Count == 0)
            return true;

        return legs.All(s => ResolvedLegStates.Contains((s ?? "").Trim()));
    }
}
