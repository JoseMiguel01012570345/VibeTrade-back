using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Search;
using VibeTrade.Backend.Features.SavedOffers;
using VibeTrade.Backend.Api.Swagger;
using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Utils.TimeZone;
void TryLoadEnv(string path)
{
    if (File.Exists(path))
        Env.Load(path);
}

TryLoadEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
TryLoadEnv(Path.Combine(builder.Environment.ContentRootPath, ".env"));

const long maxRequestBodyBytes = 524_288_000L; // 500 MiB
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

var connectionString = PostgresConfiguration.BuildConnectionString();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddScoped<RequestTimeZoneContext>();
builder.Services.AddSingleton<IMarketWorkspaceIntegrity, MarketWorkspaceIntegrity>();
builder.Services.AddScoped<IMarketWorkspaceRepository, MarketWorkspaceRepository>();
builder.Services.AddScoped<IMarketCatalogSyncService, MarketCatalogSyncService>();
builder.Services.AddScoped<IOfferEngagementService, OfferEngagementService>();
builder.Services.AddScoped<IMarketWorkspaceService, MarketWorkspaceService>();
builder.Services.AddScoped<IMarketCatalogStoreSearchService, MarketCatalogStoreSearchService>();
builder.Services.AddScoped<IBootstrapService, BootstrapService>();
builder.Services.AddScoped<IGuestBootstrapService, GuestBootstrapService>();
builder.Services.AddScoped<ISavedOffersService, SavedOffersService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddSingleton<IGuestInteractionStore, GuestInteractionStore>();
builder.Services.AddScoped<IGuestRecommendationService, GuestRecommendationService>();
builder.Services.AddScoped<IUserAccountSyncService, UserAccountSyncService>();
builder.Services.AddScoped<IUserContactsService, UserContactsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddMemoryCache();

builder.Services.Configure<ElasticsearchStoreSearchOptions>(
    builder.Configuration.GetSection(ElasticsearchStoreSearchOptions.SectionName));
builder.Services.AddSingleton<IStoreSearchTextEmbeddingService, StoreSearchMlNetTfIdfEmbeddingService>();
builder.Services.AddScoped<IElasticsearchStoreSearchQuery, ElasticsearchStoreSearchQuery>();
builder.Services.AddScoped<IStoreSearchIndexWriter, ElasticsearchStoreSearchIndexWriter>();
builder.Services.AddHostedService<ElasticsearchSearchStartupHostedService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "VibeTrade API",
        Version = "v1",
        Description =
            "REST API for the VibeTrade web client. "
            + "Send the **X-Timezone** header (IANA id, e.g. `America/Argentina/Buenos_Aires`) on requests so the server can interpret dates in UTC (flow-ui). "
            + "Health: `GET /health` (JSON; 503 si la base u otra dependencia falla). "
            + "Swagger UI: **http://localhost:5110/swagger** o **http://127.0.0.1:5110/swagger** (puerto 5110 = solo HTTP).",
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
        o.IncludeXmlComments(xml, includeControllerXmlComments: true);
    o.OperationFilter<XTimezoneHeaderOperationFilter>();
});

builder.Services.AddCors(o =>
{
    o.AddPolicy("Dev", p =>
        p.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod()
            // SignalR negotiate + WebSockets from Vite (otro puerto) lo requieren.
            .AllowCredentials());
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();


// Migrations
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

await using (var seedScope = app.Services.CreateAsyncScope())
{
    var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seedCfg = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedLog = seedScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("VibeTrade.Backend.Infrastructure.DemoDataSeed");
    await DemoDataSeed.RunIfNeededAsync(seedDb, seedCfg, seedLog);
}

app.UseRouting();
app.UseCors("Dev");

app.UseMiddleware<BearerSessionAuthMiddleware>();

// Swagger: por defecto activo (evita 404 si ASPNETCORE_ENVIRONMENT no es Development).
// Desactivar en producción con Swagger:Enabled=false o appsettings.Production.json.
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

app.UseMiddleware<TimeZoneHeaderMiddleware>();
app.MapControllers();
app.MapHub<ChatHub>("/ws/chat").RequireCors("Dev");

app.Run();
