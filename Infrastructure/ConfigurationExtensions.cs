using DotNetEnv;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.Routing.Interfaces;

namespace VibeTrade.Backend.Infrastructure;

public static class ConfigurationExtensions
{
    public static void LoadVibeTradeEnvironment(this WebApplicationBuilder builder)
    {
        TryLoadEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
        TryLoadEnv(Path.Combine(builder.Environment.ContentRootPath, ".env"));

        var emailSmtpPassword = Environment.GetEnvironmentVariable("EMAIL_SMTP_PASSWORD");
        if (!string.IsNullOrEmpty(emailSmtpPassword))
            builder.Configuration["EmailSmtp:Password"] = emailSmtpPassword;

        var graphHopperApiKey = Environment.GetEnvironmentVariable("GraphHopper_ApiKey")?.Trim();
        if (!string.IsNullOrEmpty(graphHopperApiKey))
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{RoutingOptions.SectionName}:{nameof(RoutingOptions.GraphHopperApiKey)}"] =
                        graphHopperApiKey,
                });
        }
    }

    private static void TryLoadEnv(string path)
    {
        if (File.Exists(path))
            Env.Load(path);
    }
}
