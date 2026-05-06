using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Infrastructure.DemoData;

namespace VibeTrade.Backend.Infrastructure;

public static class ApplicationBuilderExtensions
{
    public static async Task<WebApplication> InitializeVibeTradeDatabaseAsync(this WebApplication app)
    {
        const int migrateMaxAttempts = 15;
        var migrateDelay = TimeSpan.FromSeconds(2);
        Exception? migrateError = null;
        for (var attempt = 1; attempt <= migrateMaxAttempts; attempt++)
        {
            try
            {
                await using var migrateScope = app.Services.CreateAsyncScope();
                var db = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                migrateError = null;
                break;
            }
            catch (Exception ex)
            {
                migrateError = ex;
                if (attempt == migrateMaxAttempts)
                    break;
                await Task.Delay(migrateDelay);
            }
        }

        if (migrateError is not null)
            throw new InvalidOperationException(
                $"Database migration failed after {migrateMaxAttempts} attempts. Is PostgreSQL running and reachable?",
                migrateError);

        await using var seedScope = app.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seedCfg = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
        var seedLog = seedScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("VibeTrade.Backend.Infrastructure.DemoData.DemoDataSeed");
        var hostEnv = seedScope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        await DemoDataSeed.RunIfNeededAsync(seedDb, seedCfg, seedLog, hostEnv);

        return app;
    }

    public static WebApplication UseVibeTradePipeline(this WebApplication app)
    {
        app.UseRouting();
        app.UseCors("Dev");

        app.UseMiddleware<BearerSessionAuthMiddleware>();

        if (app.Configuration.GetValue("Swagger:Enabled", true))
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "VibeTrade API v1");
                c.DocumentTitle = "VibeTrade API";
                c.RoutePrefix = "swagger";
            });
        }

        app.MapControllers();
        app.MapHub<ChatHub>("/ws/chat").RequireCors("Dev");

        return app;
    }
}
