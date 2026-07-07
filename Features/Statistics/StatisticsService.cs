using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Orders;
using VibeTrade.Backend.Features.Statistics.Dtos;
using VibeTrade.Backend.Features.Statistics.Interfaces;
using VibeTrade.Backend.Features.Statistics.Utils;

namespace VibeTrade.Backend.Features.Statistics;

public sealed class StatisticsService(AppDbContext db, IIpGeolocationService geo) : IStatisticsService
{
    private sealed record OrderProjection(
        string Id,
        string PublicNumber,
        string Status,
        string BuyerUserId,
        string CurrencyCode,
        decimal Total,
        DateTimeOffset CreatedAtUtc,
        string? RouteSheetId,
        double? DeliveryLatitude,
        double? DeliveryLongitude);

    private IQueryable<OrderRow> BuildOrders(StatisticsQuery q, bool applyDate, bool applyDeliveredOnly)
    {
        var scope = q.StoreScope?.ToArray();
        var qy = db.Orders.AsNoTracking().AsQueryable();
        if (applyDate)
            qy = qy.Where(o => o.CreatedAtUtc >= q.From && o.CreatedAtUtc <= q.To);
        if (scope is not null)
            qy = qy.Where(o => scope.Contains(o.StoreId));
        if (!q.IncludeDeleted)
            qy = qy.Where(o => o.DeletedAtUtc == null);
        if (!q.IncludeInvalidated)
            qy = qy.Where(o => !o.IsInvalidated);
        if (applyDeliveredOnly && q.DeliveredOnly)
            qy = qy.Where(o => o.Status == OrderStatuses.Entregado);
        return qy;
    }

    private async Task<List<OrderProjection>> LoadOrdersAsync(
        StatisticsQuery q, bool applyDate, bool applyDeliveredOnly, CancellationToken ct)
    {
        return await BuildOrders(q, applyDate, applyDeliveredOnly)
            .Select(o => new OrderProjection(
                o.Id,
                o.PublicNumber,
                o.Status,
                o.BuyerUserId,
                o.CurrencyCode,
                o.Total,
                o.CreatedAtUtc,
                o.RouteSheetId,
                o.DeliveryLatitude,
                o.DeliveryLongitude))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<StatisticsCurrencyAmount> RevenueByCurrency(IEnumerable<OrderProjection> orders) =>
        orders
            .GroupBy(o => o.CurrencyCode ?? "")
            .Select(g => new StatisticsCurrencyAmount(g.Key, g.Sum(o => o.Total)))
            .OrderByDescending(x => x.Amount)
            .ToList();

    public async Task<StatisticsOverviewDto> GetOverviewAsync(StatisticsQuery query, CancellationToken ct)
    {
        var all = await LoadOrdersAsync(query, applyDate: true, applyDeliveredOnly: false, ct).ConfigureAwait(false);
        var delivered = all.Where(o => o.Status == OrderStatuses.Entregado).ToList();

        var (totalKm, tripCount, _) = await LoadTripsAsync(delivered, query, ct).ConfigureAwait(false);

        var (uniqueVisitors, pageViews) = query.IsGlobalScope
            ? await LoadTrafficTotalsAsync(query, ct).ConfigureAwait(false)
            : (0, 0);
        var productViews = await CountProductViewsAsync(query, ct).ConfigureAwait(false);

        return new StatisticsOverviewDto(
            all.Count,
            delivered.Count,
            RevenueByCurrency(delivered),
            totalKm,
            tripCount,
            uniqueVisitors,
            pageViews,
            productViews);
    }

    public async Task<StatisticsDeliveredOrdersDto> GetDeliveredOrdersAsync(StatisticsQuery query, CancellationToken ct)
    {
        var delivered = await LoadOrdersAsync(
            query with { DeliveredOnly = true }, applyDate: true, applyDeliveredOnly: true, ct).ConfigureAwait(false);
        var counts = delivered
            .GroupBy(o => StatisticsMath.FormatDate(o.CreatedAtUtc))
            .ToDictionary(g => g.Key, g => g.Count());
        var series = StatisticsMath.FillDateGaps(query.From, query.To, counts);
        return new StatisticsDeliveredOrdersDto(delivered.Count, series);
    }

    private async Task<(double TotalKm, int TripCount, List<(double Km, DateTimeOffset At)> Trips)> LoadTripsAsync(
        IReadOnlyList<OrderProjection> deliveredOrders, StatisticsQuery query, CancellationToken ct)
    {
        var routeSheetIds = deliveredOrders
            .Where(o => o.RouteSheetId != null)
            .Select(o => o.RouteSheetId!)
            .Distinct()
            .ToList();
        if (routeSheetIds.Count == 0)
            return (0, 0, new List<(double, DateTimeOffset)>());

        var calcs = await db.RouteSheetRouteCalculations.AsNoTracking()
            .Where(c => routeSheetIds.Contains(c.RouteSheetId) && c.TotalKm != null)
            .Select(c => new { c.RouteSheetId, Km = c.TotalKm!.Value, c.UpdatedAtUtc })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Una hoja puede tener varios cálculos (por hilo): quedarse con el más reciente.
        var perSheet = calcs
            .GroupBy(c => c.RouteSheetId)
            .Select(g => g.OrderByDescending(x => x.UpdatedAtUtc).First())
            .Select(x => (Km: x.Km, At: x.UpdatedAtUtc))
            .ToList();

        return (perSheet.Sum(t => t.Km), perSheet.Count, perSheet);
    }

    public async Task<StatisticsTripKmDto> GetTripKmAsync(StatisticsQuery query, CancellationToken ct)
    {
        var delivered = await LoadOrdersAsync(
            query with { DeliveredOnly = true }, applyDate: true, applyDeliveredOnly: true, ct).ConfigureAwait(false);
        var (totalKm, tripCount, trips) = await LoadTripsAsync(delivered, query, ct).ConfigureAwait(false);

        var values = trips.Select(t => t.Km).OrderBy(v => v).ToList();
        var tripsPerDay = StatisticsMath.FillDateGaps(
            query.From,
            query.To,
            trips
                .GroupBy(t => StatisticsMath.FormatDate(t.At))
                .ToDictionary(g => g.Key, g => g.Count()));

        return new StatisticsTripKmDto(
            totalKm,
            tripCount,
            values.Count > 0 ? values[0] : 0,
            StatisticsMath.Percentile(values, 0.5),
            StatisticsMath.Percentile(values, 0.9),
            StatisticsMath.BuildHistogram(values),
            tripsPerDay);
    }

    public async Task<StatisticsOrderLocationsDto> GetOrderLocationsAsync(
        StatisticsQuery query, string? status, int page, int pageSize, CancellationToken ct)
    {
        var orders = await LoadOrdersAsync(query, applyDate: true, applyDeliveredOnly: false, ct).ConfigureAwait(false);
        var located = orders
            .Where(o => o.DeliveryLatitude != null && o.DeliveryLongitude != null)
            .Where(o => string.IsNullOrWhiteSpace(status) || o.Status == status)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();

        var safePage = page < 1 ? 1 : page;
        var safeSize = Math.Clamp(pageSize, 1, 2000);
        var points = located
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(o => new StatisticsOrderLocationPoint(
                o.Id,
                o.PublicNumber,
                o.Status,
                o.DeliveryLatitude!.Value,
                o.DeliveryLongitude!.Value,
                o.CreatedAtUtc))
            .ToList();

        return new StatisticsOrderLocationsDto(located.Count, points);
    }

    public async Task<StatisticsOrderFunnelDto> GetOrderFunnelAsync(StatisticsQuery query, CancellationToken ct)
    {
        var orders = await LoadOrdersAsync(
            query with { DeliveredOnly = false }, applyDate: true, applyDeliveredOnly: false, ct).ConfigureAwait(false);
        var byStatus = orders.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count());
        var order = new[] { OrderStatuses.Procesado, OrderStatuses.EnTransito, OrderStatuses.Entregado };
        var stages = order
            .Select(s => new StatisticsFunnelRow(s, byStatus.TryGetValue(s, out var c) ? c : 0))
            .ToList();
        return new StatisticsOrderFunnelDto(stages);
    }

    public async Task<StatisticsOrdersByHourDto> GetOrdersByHourAsync(StatisticsQuery query, CancellationToken ct)
    {
        var orders = await LoadOrdersAsync(query, applyDate: true, applyDeliveredOnly: false, ct).ConfigureAwait(false);
        var cells = orders
            .GroupBy(o => new
            {
                DayOfWeek = (int)o.CreatedAtUtc.UtcDateTime.DayOfWeek,
                Hour = o.CreatedAtUtc.UtcDateTime.Hour,
            })
            .Select(g => new StatisticsPeakHourCell(g.Key.DayOfWeek, g.Key.Hour, g.Count()))
            .OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour)
            .ToList();
        return new StatisticsOrdersByHourDto(cells);
    }

    public async Task<StatisticsCustomersDto> GetCustomersAsync(StatisticsQuery query, CancellationToken ct)
    {
        var periodOrders = await LoadOrdersAsync(query, applyDate: true, applyDeliveredOnly: true, ct).ConfigureAwait(false);
        var buyerIds = periodOrders.Select(o => o.BuyerUserId).Distinct().ToList();
        if (buyerIds.Count == 0)
        {
            var empty = Array.Empty<StatisticsDateSeriesPoint>();
            return new StatisticsCustomersDto(0, 0, empty, empty);
        }

        // Primer pedido histórico (dentro del scope) de cada comprador para decidir nuevo vs recurrente.
        var earliest = await BuildOrders(query, applyDate: false, applyDeliveredOnly: true)
            .Where(o => buyerIds.Contains(o.BuyerUserId))
            .GroupBy(o => o.BuyerUserId)
            .Select(g => new { BuyerUserId = g.Key, First = g.Min(o => o.CreatedAtUtc) })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var firstByBuyer = earliest.ToDictionary(x => x.BuyerUserId, x => x.First);

        var newPerDay = new Dictionary<string, int>();
        var returningPerDay = new Dictionary<string, int>();
        var newCount = 0;
        var returningCount = 0;

        foreach (var buyer in buyerIds)
        {
            var firstInPeriod = periodOrders
                .Where(o => o.BuyerUserId == buyer)
                .Min(o => o.CreatedAtUtc);
            var key = StatisticsMath.FormatDate(firstInPeriod);
            var isNew = firstByBuyer.TryGetValue(buyer, out var first) && first >= query.From;
            if (isNew)
            {
                newCount++;
                newPerDay[key] = newPerDay.GetValueOrDefault(key) + 1;
            }
            else
            {
                returningCount++;
                returningPerDay[key] = returningPerDay.GetValueOrDefault(key) + 1;
            }
        }

        return new StatisticsCustomersDto(
            newCount,
            returningCount,
            StatisticsMath.FillDateGaps(query.From, query.To, newPerDay),
            StatisticsMath.FillDateGaps(query.From, query.To, returningPerDay));
    }

    public async Task<StatisticsCancellationsDto> GetCancellationsAsync(StatisticsQuery query, CancellationToken ct)
    {
        var scope = query.StoreScope?.ToArray();
        var baseQuery = db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= query.From && o.CreatedAtUtc <= query.To);
        if (scope is not null)
            baseQuery = baseQuery.Where(o => scope.Contains(o.StoreId));

        var total = await baseQuery.CountAsync(ct).ConfigureAwait(false);
        var invalidated = await baseQuery.CountAsync(o => o.IsInvalidated, ct).ConfigureAwait(false);
        var deleted = await baseQuery.CountAsync(o => o.DeletedAtUtc != null, ct).ConfigureAwait(false);

        var invRate = total > 0 ? Math.Round((double)invalidated / total * 100, 2) : 0;
        var delRate = total > 0 ? Math.Round((double)deleted / total * 100, 2) : 0;
        return new StatisticsCancellationsDto(invalidated, deleted, total, invRate, delRate);
    }

    private async Task<int> CountProductViewsAsync(StatisticsQuery query, CancellationToken ct)
    {
        var q = ScopedProductViews(query);
        return await q.CountAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<ProductViewEventRow> ScopedProductViews(StatisticsQuery query)
    {
        var q = db.ProductViewEvents.AsNoTracking()
            .Where(e => e.ViewedAt >= query.From && e.ViewedAt <= query.To);
        var scope = query.StoreScope?.ToArray();
        if (scope is not null)
        {
            q = q.Where(e => db.StoreProducts.Any(p => p.Id == e.ProductId && scope.Contains(p.StoreId)));
        }

        return q;
    }

    public async Task<StatisticsProductViewsDto> GetProductViewsAsync(StatisticsQuery query, int limit, CancellationToken ct)
    {
        var events = await ScopedProductViews(query)
            .Select(e => new { e.ProductId, e.ViewedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var viewsPerDay = StatisticsMath.FillDateGaps(
            query.From,
            query.To,
            events.GroupBy(e => StatisticsMath.FormatDate(e.ViewedAt)).ToDictionary(g => g.Key, g => g.Count()));

        var safeLimit = Math.Clamp(limit, 1, 100);
        var topGroups = events
            .GroupBy(e => e.ProductId)
            .Select(g => new { ProductId = g.Key, Views = g.Count() })
            .OrderByDescending(x => x.Views)
            .Take(safeLimit)
            .ToList();

        var ids = topGroups.Select(g => g.ProductId).ToList();
        var names = await db.StoreProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var nameById = names.ToDictionary(x => x.Id, x => x.Name);

        var top = topGroups
            .Select(g => new StatisticsProductViewRow(
                g.ProductId,
                nameById.TryGetValue(g.ProductId, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n
                    : $"Producto {g.ProductId}",
                g.Views))
            .ToList();

        return new StatisticsProductViewsDto(events.Count, viewsPerDay, top);
    }

    private async Task<(int UniqueVisitors, int PageViews)> LoadTrafficTotalsAsync(StatisticsQuery query, CancellationToken ct)
    {
        var views = db.AnalyticsPageViews.AsNoTracking()
            .Where(v => v.ViewedAt >= query.From && v.ViewedAt <= query.To);
        var pageViews = await views.CountAsync(ct).ConfigureAwait(false);
        var unique = await views.Select(v => v.SessionKey).Distinct().CountAsync(ct).ConfigureAwait(false);
        return (unique, pageViews);
    }

    public async Task<StatisticsTrafficDto> GetTrafficAsync(StatisticsQuery query, CancellationToken ct)
    {
        if (!query.IsGlobalScope)
        {
            var empty = Array.Empty<StatisticsDateSeriesPoint>();
            return new StatisticsTrafficDto(0, 0, empty, empty,
                Array.Empty<StatisticsTrafficPathRow>(), Array.Empty<StatisticsTrafficIpRow>());
        }

        var views = await db.AnalyticsPageViews.AsNoTracking()
            .Where(v => v.ViewedAt >= query.From && v.ViewedAt <= query.To)
            .Select(v => new { v.SessionKey, v.IpAddress, v.Path, v.ViewedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pageViewsPerDay = StatisticsMath.FillDateGaps(
            query.From, query.To,
            views.GroupBy(v => StatisticsMath.FormatDate(v.ViewedAt)).ToDictionary(g => g.Key, g => g.Count()));

        var uniquePerDay = StatisticsMath.FillDateGaps(
            query.From, query.To,
            views.GroupBy(v => StatisticsMath.FormatDate(v.ViewedAt))
                .ToDictionary(g => g.Key, g => g.Select(x => x.SessionKey).Distinct().Count()));

        var topPaths = views
            .GroupBy(v => v.Path)
            .Select(g => new StatisticsTrafficPathRow(g.Key, g.Count()))
            .OrderByDescending(x => x.Views)
            .Take(15)
            .ToList();

        var topIps = views
            .GroupBy(v => v.IpAddress)
            .Select(g => new StatisticsTrafficIpRow(
                g.Key,
                geo.GetDisplayLabel(g.Key),
                g.Select(x => x.SessionKey).Distinct().Count(),
                g.Count()))
            .OrderByDescending(x => x.PageViews)
            .Take(15)
            .ToList();

        return new StatisticsTrafficDto(
            views.Select(v => v.SessionKey).Distinct().Count(),
            views.Count,
            uniquePerDay,
            pageViewsPerDay,
            topPaths,
            topIps);
    }

    public async Task<StatisticsLandingExitDto> GetLandingExitAsync(StatisticsQuery query, int limit, CancellationToken ct)
    {
        if (!query.IsGlobalScope)
            return new StatisticsLandingExitDto(
                Array.Empty<StatisticsLandingExitRow>(), Array.Empty<StatisticsLandingExitRow>());

        var views = await db.AnalyticsPageViews.AsNoTracking()
            .Where(v => v.ViewedAt >= query.From && v.ViewedAt <= query.To)
            .Select(v => new { v.SessionKey, v.Path, v.ViewedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var safeLimit = Math.Clamp(limit, 1, 50);
        var landing = new Dictionary<string, int>();
        var exit = new Dictionary<string, int>();

        foreach (var session in views.GroupBy(v => v.SessionKey))
        {
            var ordered = session.OrderBy(v => v.ViewedAt).ToList();
            var first = ordered[0].Path;
            var last = ordered[^1].Path;
            landing[first] = landing.GetValueOrDefault(first) + 1;
            exit[last] = exit.GetValueOrDefault(last) + 1;
        }

        var landingRows = landing
            .Select(kv => new StatisticsLandingExitRow(kv.Key, kv.Value, true))
            .OrderByDescending(x => x.Count).Take(safeLimit).ToList();
        var exitRows = exit
            .Select(kv => new StatisticsLandingExitRow(kv.Key, kv.Value, false))
            .OrderByDescending(x => x.Count).Take(safeLimit).ToList();

        return new StatisticsLandingExitDto(landingRows, exitRows);
    }

    public async Task<StatisticsRevenueAveragesDto> GetRevenueAveragesAsync(StatisticsQuery query, CancellationToken ct)
    {
        var delivered = await LoadOrdersAsync(
            query with { DeliveredOnly = true }, applyDate: true, applyDeliveredOnly: true, ct).ConfigureAwait(false);

        var currency = delivered
            .GroupBy(o => o.CurrencyCode ?? "")
            .OrderByDescending(g => g.Sum(o => o.Total))
            .Select(g => g.Key)
            .FirstOrDefault() ?? "";

        var forCurrency = delivered.Where(o => (o.CurrencyCode ?? "") == currency).ToList();

        var dailyAmounts = forCurrency
            .GroupBy(o => StatisticsMath.FormatDate(o.CreatedAtUtc))
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));
        var daily = StatisticsMath.FillDecimalDateGaps(query.From, query.To, dailyAmounts);

        var monthlyAmounts = forCurrency
            .GroupBy(o => o.CreatedAtUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));
        var monthly = StatisticsMath.FillMonthGaps(query.From, query.To, monthlyAmounts);

        var hourlyAmounts = forCurrency
            .GroupBy(o => o.CreatedAtUtc.UtcDateTime.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));
        var hourly = new List<StatisticsRevenueAverageBucket>(24);
        for (var h = 0; h < 24; h++)
        {
            hourlyAmounts.TryGetValue(h, out var amount);
            hourly.Add(new StatisticsRevenueAverageBucket($"{h:00}:00", amount));
        }

        return new StatisticsRevenueAveragesDto(
            new StatisticsRevenueAverageSeries("daily", currency, daily, Average(daily)),
            new StatisticsRevenueAverageSeries("monthly", currency, monthly, Average(monthly)),
            new StatisticsRevenueAverageSeries("hourly", currency, hourly, Average(hourly)));
    }

    private static decimal Average(IReadOnlyList<StatisticsRevenueAverageBucket> series) =>
        series.Count == 0 ? 0 : Math.Round(series.Average(b => b.Amount), 2);
}
