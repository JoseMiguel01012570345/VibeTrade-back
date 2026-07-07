using Microsoft.Extensions.Options;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.Routing.Services;
using VibeTrade.Backend.Infrastructure.Routing;

namespace VibeTrade.Backend.Features.Routing;

public static class RoutingModule
{
    public static IServiceCollection AddRoutingFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RoutingOptions>(
            configuration.GetSection(RoutingOptions.SectionName));
        services.AddHttpClient<IGraphHopperRoutingClient, GraphHopperDrivingLegService>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeTradeBackend/1.0");
            var opt = sp.GetRequiredService<IOptions<RoutingOptions>>().Value;
            var baseUrl = (opt.GraphHopperBaseUrl ?? "").Trim();
            if (baseUrl.Length == 0)
                return;
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                client.BaseAddress = uri;
        });
        services.AddScoped<IDrivingLegRoutingService>(sp => sp.GetRequiredService<IGraphHopperRoutingClient>());

        services.AddScoped<IRouteBackgroundJobEnqueueService, RouteBackgroundJobEnqueueService>();
        services.AddScoped<IRouteSheetRoutingMatrixService, RouteSheetRoutingMatrixService>();
        services.AddScoped<IRouteSheetTourPlanningService, RouteSheetTourPlanningService>();
        services.AddScoped<RouteBackgroundJobProcessor>();
        services.AddHostedService<RouteBackgroundJobWorker>();
        return services;
    }
}