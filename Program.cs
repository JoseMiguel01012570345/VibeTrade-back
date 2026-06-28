using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Infrastructure.Mediator;

var builder = WebApplication.CreateBuilder(args);
builder.LoadVibeTradeEnvironment();

const long maxRequestBodyBytes = 524_288_000L; // 500 MiB
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

builder.Services
    .AddVibeTradePersistence()
    .AddVibeTradeMediatR()
    .AddVibeTradeFeatures(builder.Configuration)
    .AddVibeTradeApi();

var app = builder.Build();

await app.InitializeVibeTradeDatabaseAsync();
app.UseVibeTradePipeline();

app.Run();
