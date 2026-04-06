using System.Reflection;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Market;
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

var connectionString = PostgresConfiguration.BuildConnectionString();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<RequestTimeZoneContext>();
builder.Services.AddSingleton<IMarketWorkspaceIntegrity, MarketWorkspaceIntegrity>();
builder.Services.AddScoped<IMarketWorkspaceRepository, MarketWorkspaceRepository>();
builder.Services.AddScoped<IMarketWorkspaceService, MarketWorkspaceService>();
builder.Services.AddScoped<IBootstrapService, BootstrapService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

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
        p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

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

// Carga en BD el workspace desde Mocks/market-workspace.json si la tabla está vacía.
await using (var seedScope = app.Services.CreateAsyncScope())
{
    var marketSeed = seedScope.ServiceProvider.GetRequiredService<IMarketWorkspaceService>();
    await marketSeed.GetOrSeedAsync();
}

app.UseCors("Dev");

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

app.Run();
