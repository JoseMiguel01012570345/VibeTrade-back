using VibeTrade.Backend.Features.Catalog.Interfaces;

namespace VibeTrade.Backend.Features.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogFeature(this IServiceCollection services)
    {
        services.AddScoped<IMarketCatalogSyncService, CatalogService>();
        return services;
    }
}
