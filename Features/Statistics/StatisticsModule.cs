using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Statistics.Dtos;
using VibeTrade.Backend.Features.Statistics.Interfaces;
using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Infrastructure.Interfaces;

namespace VibeTrade.Backend.Features.Statistics;

public static class StatisticsModule
{
    public static IServiceCollection AddStatisticsFeature(this IServiceCollection services)
    {
        services.AddSingleton<IIpGeolocationService, IpGeolocationService>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        return services;
    }

    public static WebApplication MapStatisticsEndpoints(this WebApplication app)
    {
        const string tag = "Statistics";
        var group = app.MapGroup("/api/v1/statistics").WithTags(tag);

        group.MapGet("/overview", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool deliveredOnly, bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, deliveredOnly, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetOverviewAsync(query!, ct));
        });

        group.MapGet("/delivered-orders", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, false, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetDeliveredOrdersAsync(query!, ct));
        });

        group.MapGet("/trip-km", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, true, false, false, ct);
            return error ?? Results.Ok(await stats.GetTripKmAsync(query!, ct));
        });

        group.MapGet("/order-locations", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool deliveredOnly, bool includeInvalidated, bool includeDeleted, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, deliveredOnly, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetOrderLocationsAsync(query!, status, page ?? 1, pageSize ?? 500, ct));
        });

        group.MapGet("/traffic", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, true, false, false, ct);
            return error ?? Results.Ok(await stats.GetTrafficAsync(query!, ct));
        });

        group.MapGet("/traffic/landing-exit", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, true, false, false, ct);
            return error ?? Results.Ok(await stats.GetLandingExitAsync(query!, limit ?? 15, ct));
        });

        group.MapGet("/product-views", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, true, false, false, ct);
            return error ?? Results.Ok(await stats.GetProductViewsAsync(query!, limit ?? 15, ct));
        });

        group.MapGet("/order-funnel", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, false, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetOrderFunnelAsync(query!, ct));
        });

        group.MapGet("/orders-by-hour", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool deliveredOnly, bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, deliveredOnly, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetOrdersByHourAsync(query!, ct));
        });

        group.MapGet("/customers", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool deliveredOnly, bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, deliveredOnly, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetCustomersAsync(query!, ct));
        });

        group.MapGet("/cancellations", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, false, true, true, ct);
            return error ?? Results.Ok(await stats.GetCancellationsAsync(query!, ct));
        });

        group.MapGet("/revenue/averages", async (
            HttpRequest request, IStatisticsService stats, ICurrentUserAccessor currentUser, IUserRoleService roles,
            AppDbContext db, DateTimeOffset? from, DateTimeOffset? to,
            bool deliveredOnly, bool includeInvalidated, bool includeDeleted, CancellationToken ct) =>
        {
            var (query, error) = await ResolveAsync(request, currentUser, roles, db, from, to, deliveredOnly, includeInvalidated, includeDeleted, ct);
            return error ?? Results.Ok(await stats.GetRevenueAveragesAsync(query!, ct));
        });

        return app;
    }

    /// <summary>Gatea a admin/superadmin, valida el rango y resuelve el scope de tiendas (null = todo, para superadmin).</summary>
    private static async Task<(StatisticsQuery? Query, IResult? Error)> ResolveAsync(
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IUserRoleService roles,
        AppDbContext db,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool deliveredOnly,
        bool includeInvalidated,
        bool includeDeleted,
        CancellationToken ct)
    {
        var gate = await currentUser.RequireRolesAsync(request, roles, ct, RoleNames.SuperAdmin, RoleNames.Admin);
        if (!gate.Ok)
            return (null, gate.Error);

        if (!StatisticsQueryParser.TryParse(from, to, out var query, out var err))
            return (null, Results.BadRequest(new { error = "invalid_range", message = err }));

        query = StatisticsQueryParser.WithFlags(query, deliveredOnly, includeInvalidated, includeDeleted);

        var isSuper = await roles.HasAnyRoleAsync(gate.UserId, new[] { RoleNames.SuperAdmin }, ct).ConfigureAwait(false);
        IReadOnlyCollection<string>? scope = null;
        if (!isSuper)
        {
            scope = await db.Stores.AsNoTracking()
                .Where(s => s.OwnerUserId == gate.UserId && s.DeletedAtUtc == null)
                .Select(s => s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        query = StatisticsQueryParser.WithScope(query, scope);
        return (query, null);
    }
}
