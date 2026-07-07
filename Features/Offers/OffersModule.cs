using VibeTrade.Backend.Features.Offers.Interfaces;

namespace VibeTrade.Backend.Features.Offers;

public static class OffersModule
{
    public static IServiceCollection AddOffersFeature(this IServiceCollection services)
    {
        services.AddScoped<IOfferService, OfferService>();
        return services;
    }
}
