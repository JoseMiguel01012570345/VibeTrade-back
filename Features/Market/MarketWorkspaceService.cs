using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

public sealed class MarketWorkspaceService(
    IMarketWorkspaceRepository repository,
    IMarketWorkspaceIntegrity integrity,
    IWebHostEnvironment environment) : IMarketWorkspaceService
{
    private static string MarketMockPath(IWebHostEnvironment env) =>
        Path.Combine(env.ContentRootPath, "Mocks", "market-workspace.json");

    public async Task<JsonDocument> GetOrSeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is not null) return existing;

        var seed = LoadSeedFromMocksFolder();
        integrity.ValidateOrThrow(seed);
        await repository.SaveAsync(seed, cancellationToken);
        return seed;
    }

    public async Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default)
    {
        integrity.ValidateOrThrow(document);
        await repository.SaveAsync(document, cancellationToken);
    }

    private JsonDocument LoadSeedFromMocksFolder()
    {
        var path = MarketMockPath(environment);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Mock file not found: {path}. Add Mocks/market-workspace.json or set CopyToOutputDirectory.");

        var text = File.ReadAllText(path);
        return JsonDocument.Parse(text);
    }
}
