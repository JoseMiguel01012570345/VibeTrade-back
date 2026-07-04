using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Offers;
using VibeTrade.Backend.Features.Offers.Interfaces;

namespace VibeTrade.Backend.Features.Market;

public static partial class MarketModule
{
    public static IServiceCollection AddMarketFeature(this IServiceCollection services)
    {
        services.AddScoped<IMarketWorkspaceService, MarketService>();
        services.AddScoped<IStoreCommentsService, StoreCommentsService>();
        services.AddScoped<IStoreCatalogSearchService, StoreCatalogSearchService>();
        return services;
    }
}
