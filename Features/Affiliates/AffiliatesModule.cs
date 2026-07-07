using VibeTrade.Backend.Features.Affiliates.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Features.Affiliates;

public static class AffiliatesModule
{
    public static IServiceCollection AddAffiliatesFeature(this IServiceCollection services)
    {
        services.AddScoped<IAffiliateService, AffiliateService>();
        return services;
    }

    public static WebApplication MapAffiliatesEndpoints(this WebApplication app)
    {
        const string tag = "Affiliates";

        app.MapPost("/api/v1/affiliates/{code}/visit", RegisterVisitAsync).WithTags(tag);
        app.MapGet("/api/v1/affiliates/mine", GetMyDashboardsAsync).WithTags(tag);

        return app;
    }

    private static async Task<IResult> RegisterVisitAsync(
        string code,
        IAffiliateService affiliates,
        CancellationToken cancellationToken)
    {
        await affiliates.RegisterVisitAsync(code, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> GetMyDashboardsAsync(
        HttpRequest request,
        IAffiliateService affiliates,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var dashboards = await affiliates.GetDashboardsForOwnerAsync(userId.Trim(), cancellationToken).ConfigureAwait(false);
        return Results.Ok(dashboards);
    }
}
