using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Debts.Dtos;
using VibeTrade.Backend.Features.Debts.Entities;
using VibeTrade.Backend.Features.Debts.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Logistics.Entities;

namespace VibeTrade.Backend.Features.Debts;

/// <summary>
/// Implementa las deudas de mercancía adaptando la lógica de <c>DebtsService</c>/<c>AdminOrderService</c>
/// de la referencia a las convenciones de VibeTrade (IDs string, tarifa por tienda, sin proveedor FX:
/// se totaliza por moneda en lugar de convertir).
/// </summary>
public sealed class DebtsService(AppDbContext db) : IDebtsService
{
    public async Task RecordWarehouseAndAffiliateDebtsAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var oid = (orderId ?? "").Trim();
        if (oid.Length == 0)
            return;

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == oid, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return;

        var now = DateTimeOffset.UtcNow;
        var changed = false;

        if (!order.WarehouseDebtsRecorded && order.Subtotal > 0)
        {
            db.WarehouseDebts.Add(new WarehouseDebtRow
            {
                Id = "wdebt_" + Guid.NewGuid().ToString("N"),
                StoreId = order.StoreId,
                OrderId = order.Id,
                OrderPublicNumber = order.PublicNumber,
                Amount = order.Subtotal,
                CurrencyCode = order.CurrencyCode,
                CreatedAtUtc = now,
            });
            order.WarehouseDebtsRecorded = true;
            changed = true;
        }

        var affiliateCode = (order.AffiliateCodeSnapshot ?? "").Trim();
        if (!order.AffiliateDebtRecorded && affiliateCode.Length > 0 && order.AffiliateCommissionAmount is > 0)
        {
            var affiliateId = await db.Affiliates.AsNoTracking()
                .Where(a => a.Code == affiliateCode)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            db.AffiliateDebts.Add(new AffiliateDebtRow
            {
                Id = "adebt_" + Guid.NewGuid().ToString("N"),
                AffiliateId = string.IsNullOrWhiteSpace(affiliateId) ? null : affiliateId,
                AffiliateCode = affiliateCode,
                OrderId = order.Id,
                OrderPublicNumber = order.PublicNumber,
                Amount = order.AffiliateCommissionAmount!.Value,
                CurrencyCode = order.CurrencyCode,
                CreatedAtUtc = now,
            });
            order.AffiliateDebtRecorded = true;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordCarrierDebtsOnDeliveredAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var oid = (orderId ?? "").Trim();
        if (oid.Length == 0)
            return;

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == oid, cancellationToken).ConfigureAwait(false);
        if (order is null || order.CarrierDebtRecorded)
            return;

        var sheet = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == oid && r.DeletedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (sheet is null)
        {
            order.CarrierDebtRecorded = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == order.StoreId, cancellationToken)
            .ConfigureAwait(false);
        var ratePerKm = store?.PricePerKm ?? 0m;
        var currency = string.IsNullOrWhiteSpace(store?.PricePerKmCurrencyCode)
            ? order.CurrencyCode
            : store!.PricePerKmCurrencyCode!.Trim();

        var subs = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(s => s.ThreadId == sheet.ThreadId && s.RouteSheetId == sheet.RouteSheetId && s.Status == "confirmed")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var paradas = (sheet.Payload.Paradas ?? new List<RouteStopPayload>())
            .OrderBy(p => p.Orden)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        foreach (var parada in paradas)
        {
            var stopId = (parada.Id ?? "").Trim();
            if (stopId.Length == 0)
                continue;
            var carrier = subs
                .Where(s => string.Equals((s.StopId ?? "").Trim(), stopId, StringComparison.Ordinal))
                .Select(s => (s.CarrierUserId ?? "").Trim())
                .FirstOrDefault(c => c.Length >= 2);
            if (carrier is not { Length: >= 2 })
                continue;

            var km = parada.OsrmRoadKm ?? 0;
            if (km <= 0 && ratePerKm <= 0)
                continue;

            var amount = Math.Round((decimal)km * ratePerKm, 2, MidpointRounding.AwayFromZero);
            db.CarrierDebts.Add(new CarrierDebtRow
            {
                Id = "cdebt_" + Guid.NewGuid().ToString("N"),
                CarrierUserId = carrier,
                OrderId = order.Id,
                OrderPublicNumber = order.PublicNumber,
                RouteSheetId = sheet.RouteSheetId,
                RouteStopId = stopId,
                TotalKm = km,
                RatePerKm = ratePerKm,
                Amount = amount,
                CurrencyCode = currency,
                Liquidated = true,
                LiquidatedAtUtc = now,
                CreatedAtUtc = now,
            });
        }

        order.CarrierDebtRecorded = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DebtsOverviewDto> GetOverviewAsync(
        bool includeLiquidated,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await db.WarehouseDebts.AsNoTracking()
            .Where(d => (includeDeleted && d.Deleted) || (!d.Deleted && (!d.Liquidated || includeLiquidated)))
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var affiliate = await db.AffiliateDebts.AsNoTracking()
            .Where(d => (includeDeleted && d.Deleted) || (!d.Deleted && (!d.Liquidated || includeLiquidated)))
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var carrier = await db.CarrierDebts.AsNoTracking()
            .Where(d => (includeDeleted && d.Deleted) || (!d.Deleted && (!d.Liquidated || includeLiquidated)))
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = new List<(string Currency, decimal Amount)>();
        pending.AddRange(warehouse.Where(d => !d.Liquidated && !d.Deleted).Select(d => (d.CurrencyCode, d.Amount)));
        pending.AddRange(affiliate.Where(d => !d.Liquidated && !d.Deleted).Select(d => (d.CurrencyCode, d.Amount)));

        var liquidated = new List<(string Currency, decimal Amount)>();
        liquidated.AddRange(warehouse.Where(d => d.Liquidated && !d.Deleted).Select(d => (d.CurrencyCode, d.Amount)));
        liquidated.AddRange(affiliate.Where(d => d.Liquidated && !d.Deleted).Select(d => (d.CurrencyCode, d.Amount)));
        liquidated.AddRange(carrier.Where(d => d.Liquidated && !d.Deleted).Select(d => (d.CurrencyCode, d.Amount)));

        return new DebtsOverviewDto(
            warehouse.Select(MapWarehouse).ToList(),
            affiliate.Select(MapAffiliate).ToList(),
            carrier.Select(MapCarrier).ToList(),
            SumByCurrency(pending),
            SumByCurrency(liquidated));
    }

    public async Task<(LiquidateDebtsResponse? Value, DebtError? Error)> LiquidateAsync(
        LiquidateDebtsRequest request,
        CancellationToken cancellationToken = default)
    {
        var warehouseIds = (request?.WarehouseDebtIds ?? Array.Empty<string>())
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var affiliateIds = (request?.AffiliateDebtIds ?? Array.Empty<string>())
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

        if (warehouseIds.Length == 0 && affiliateIds.Length == 0)
            return (null, new DebtError("empty", "Indique al menos una deuda a liquidar."));

        var now = DateTimeOffset.UtcNow;
        var liquidatedWarehouse = 0;
        var liquidatedAffiliate = 0;

        if (warehouseIds.Length > 0)
            liquidatedWarehouse = await db.WarehouseDebts
                .Where(d => warehouseIds.Contains(d.Id) && !d.Liquidated && !d.Deleted)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.Liquidated, true).SetProperty(d => d.LiquidatedAtUtc, now),
                    cancellationToken)
                .ConfigureAwait(false);

        if (affiliateIds.Length > 0)
            liquidatedAffiliate = await db.AffiliateDebts
                .Where(d => affiliateIds.Contains(d.Id) && !d.Liquidated && !d.Deleted)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.Liquidated, true).SetProperty(d => d.LiquidatedAtUtc, now),
                    cancellationToken)
                .ConfigureAwait(false);

        if (liquidatedWarehouse == 0 && liquidatedAffiliate == 0)
            return (null, new DebtError("nothing_liquidated", "No había deudas pendientes con esos ids."));

        return (new LiquidateDebtsResponse(liquidatedWarehouse, liquidatedAffiliate), null);
    }

    private static IReadOnlyList<DebtCurrencyTotalDto> SumByCurrency(IEnumerable<(string Currency, decimal Amount)> rows) =>
        rows
            .GroupBy(r => (r.Currency ?? "").Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .Select(g => new DebtCurrencyTotalDto(g.Key, Math.Round(g.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero)))
            .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal)
            .ToList();

    private static WarehouseDebtDto MapWarehouse(WarehouseDebtRow d) =>
        new(d.Id, d.StoreId, d.OrderId, d.OrderPublicNumber, d.Amount, d.CurrencyCode, d.Liquidated, d.Deleted, d.CreatedAtUtc);

    private static AffiliateDebtDto MapAffiliate(AffiliateDebtRow d) =>
        new(d.Id, d.AffiliateId, d.AffiliateCode, d.OrderId, d.OrderPublicNumber, d.Amount, d.CurrencyCode, d.Liquidated, d.Deleted, d.CreatedAtUtc);

    private static CarrierDebtDto MapCarrier(CarrierDebtRow d) =>
        new(d.Id, d.CarrierUserId, d.OrderId, d.OrderPublicNumber, d.RouteSheetId, d.RouteStopId, d.TotalKm, d.RatePerKm, d.Amount, d.CurrencyCode, d.Liquidated, d.Deleted, d.CreatedAtUtc);
}
