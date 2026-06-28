using VibeTrade.Backend.Features.RouteTramoSubscriptions.Interfaces;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

public static class RouteTramoSubscriptionsModule
{
    public static IServiceCollection AddRouteTramoSubscriptionsFeature(this IServiceCollection services)
    {
        services.AddScoped<RouteTramoSubscriptionServiceCore>();
        services.AddScoped<IRouteTramoSubscriptionService, RouteTramoSubscriptionService>();
        return services;
    }
}
