using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Infrastructure;

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

app.UseHttpsRedirection();

app.MapHealthChecks("/health");

app.MapGet("/weatherforecast", (AppDbContext db) =>
{
    return db.WeatherForecasts
        .Select(p => new WeatherForecast(
            DateOnly.FromDateTime(p.Date),
            (int)p.TemperatureC,
            "N/A"
        ))
        .ToArray();
});

async Task<IResult> AddRandomWeatherForecast(AppDbContext db)
{
    var rng = Random.Shared;
    var date = DateTime.UtcNow.AddDays(rng.Next(-10, 11));
    var tempC = rng.Next(-20, 40);
    var tempF = 32 + (int)(tempC / 0.5556m);

    var row = new WeatherForecastRow
    {
        Date = date,
        TemperatureC = tempC,
        TemperatureF = tempF
    };

    db.WeatherForecasts.Add(row);
    await db.SaveChangesAsync();

    return Results.Created($"/weatherforecast/{row.Id}", row);
}

// GET for browser address bar; POST for clients (e.g. fetch).
app.MapMethods("/weatherforecast/add-random", new[] { "GET", "POST" }, AddRandomWeatherForecast);


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
