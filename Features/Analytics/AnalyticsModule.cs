using VibeTrade.Backend.Features.Analytics.Dtos;
using VibeTrade.Backend.Features.Analytics.Interfaces;

namespace VibeTrade.Backend.Features.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services)
    {
        services.AddScoped<IAnalyticsTrackingService, AnalyticsTrackingService>();
        return services;
    }

    public static WebApplication MapAnalyticsEndpoints(this WebApplication app)
    {
        const string tag = "Analytics";
        var group = app.MapGroup("/api/v1/analytics").WithTags(tag);

        group.MapPost("/page-view", PageViewAsync).AllowAnonymous();
        group.MapPost("/product-view", ProductViewAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> PageViewAsync(
        HttpRequest request,
        PageViewRequest body,
        IAnalyticsTrackingService analytics,
        CancellationToken cancellationToken)
    {
        var ip = RequestIpResolver.Resolve(request);
        var ua = request.Headers.UserAgent.ToString();
        await analytics.RecordPageViewAsync(body, ip, ua, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ProductViewAsync(
        HttpRequest request,
        ProductViewRequest body,
        IAnalyticsTrackingService analytics,
        CancellationToken cancellationToken)
    {
        var ip = RequestIpResolver.Resolve(request);
        await analytics.RecordProductViewAsync(body, ip, cancellationToken);
        return Results.NoContent();
    }
}
