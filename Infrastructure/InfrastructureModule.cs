using VibeTrade.Backend.Infrastructure.Email;
using VibeTrade.Backend.Infrastructure.Email.Interfaces;
using VibeTrade.Backend.Infrastructure.Elasticsearch;
using VibeTrade.Backend.Infrastructure.Interfaces;
using VibeTrade.Backend.Infrastructure.SignalR;
using VibeTrade.Backend.Infrastructure.Stripe;
namespace VibeTrade.Backend.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructureFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.Configure<EmailSmtpOptions>(
            configuration.GetSection(EmailSmtpOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IStripeGateway, StripeGateway>();
        services.AddSingleton<IElasticsearchSearchClient, ElasticsearchSearchClient>();
        services.AddScoped<ISignalRBroadcastAdapter, SignalRBroadcastAdapter>();
        services.AddHttpClient("linkPreview", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("VibeTradeLinkPreview/1.0");
        });
        services.AddMemoryCache();
        return services;
    }
}
