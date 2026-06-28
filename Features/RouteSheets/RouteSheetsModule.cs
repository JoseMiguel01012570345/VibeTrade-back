using VibeTrade.Backend.Features.RouteSheets.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets;

public static class RouteSheetsModule
{
    public static IServiceCollection AddRouteSheetsFeature(this IServiceCollection services)
    {
        services.AddScoped<RouteSheetsChatServiceCore>();
        services.AddScoped<IRouteSheetChatService, RouteSheetsChatService>();
        return services;
    }
}
