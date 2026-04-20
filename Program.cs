using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using VibeTrade.Backend.Api.Swagger;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Search;
using VibeTrade.Backend.Features.SavedOffers;
using Microsoft.Extensions.Hosting;
using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Infrastructure.DemoData;
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
builder.Services.Configure<OfferPopularityWeightOptions>(
    builder.Configuration.GetSection(OfferPopularityWeightOptions.SectionName));
builder.Services.AddScoped<IOfferPopularityWeightService, OfferPopularityWeightService>();
builder.Services.AddScoped<IOfferEngagementService, OfferEngagementService>();
builder.Services.AddScoped<IMarketWorkspaceService, MarketWorkspaceService>();
builder.Services.AddScoped<IMarketCatalogStoreSearchService, MarketCatalogStoreSearchService>();
builder.Services.AddScoped<IBootstrapService, BootstrapService>();
builder.Services.AddScoped<IGuestBootstrapService, GuestBootstrapService>();
builder.Services.AddScoped<ISavedOffersService, SavedOffersService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IRecommendationElasticsearchQuery, RecommendationElasticsearchQuery>();
builder.Services.AddScoped<RecommendationFeedV2>();
builder.Services.AddSingleton<IGuestInteractionStore, GuestInteractionStore>();
builder.Services.AddScoped<IGuestRecommendationService, GuestRecommendationService>();
builder.Services.AddHostedService<OfferPopularityWeightBackfillHostedService>();
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
builder.Services.AddHostedService<ElasticsearchDailyReindexHostedService>();

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
            "### Visión general\n"
            + "API REST usada por el cliente web VibeTrade: mercado, autenticación por OTP, chat, recomendaciones y medios.\n\n"
            + "### Cabeceras\n"
            + "- **`X-Timezone`**: zona horaria IANA del cliente (p. ej. `America/Havana`, `America/Argentina/Buenos_Aires`). "
            + "Recomendada en las peticiones para interpretar fechas correctamente.\n"
            + "- **`Authorization`**: `Bearer {token}` tras `POST /api/v1/auth/verify` (sesión opaca almacenada en servidor).\n\n"
            + "### Salud y entorno\n"
            + "- `GET /health` — JSON; **503** si PostgreSQL u otra dependencia falla.\n"
            + "- Swagger UI local: `http://localhost:5110/swagger` (puerto HTTP por defecto del backend).\n\n"
            + "### Convenciones\n"
            + "- Rutas bajo prefijo `api/v1/` salvo `GET /health`.\n"
            + "- Cuerpos JSON en **camelCase** (configuración ASP.NET Core).",
        Contact = new OpenApiContact
        {
            Name = "VibeTrade",
        },
    });

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description =
            "Token de sesión devuelto por `POST /api/v1/auth/verify` en el campo `sessionToken`. "
            + "Usar el botón **Authorize** y el esquema `Bearer` para probar rutas que requieren sesión.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "opaque",
    });

    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
        o.IncludeXmlComments(xml, includeControllerXmlComments: true);
    o.OperationFilter<XTimezoneHeaderOperationFilter>();
    o.DocumentFilter<TagDescriptionsDocumentFilter>();
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
        .CreateLogger("VibeTrade.Backend.Infrastructure.DemoData.DemoDataSeed");
    var hostEnv = seedScope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    await DemoDataSeed.RunIfNeededAsync(seedDb, seedCfg, seedLog, hostEnv);
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
