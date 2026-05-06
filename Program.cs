using VibeTrade.Backend.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.LoadVibeTradeEnvironment();

const long maxRequestBodyBytes = 524_288_000L; // 500 MiB
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

builder.Services
    .AddVibeTradePersistence()
    .AddVibeTradeFeatures(builder.Configuration)
    .AddVibeTradeApi();

var app = builder.Build();

await app.InitializeVibeTradeDatabaseAsync();
app.UseVibeTradePipeline();

app.Run();
