using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Features.Bootstrap;

public sealed class BootstrapService(
    IMarketWorkspaceService marketWorkspace,
    IWebHostEnvironment environment) : IBootstrapService
{
    public async Task<JsonDocument> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        using var market = await marketWorkspace.GetOrSeedAsync(cancellationToken);
        var root = environment.ContentRootPath;

        var reelsPath = Path.Combine(root, "Mocks", "reels-bootstrap.json");
        if (!File.Exists(reelsPath))
            throw new FileNotFoundException($"Mock file not found: {reelsPath}");

        var namesPath = Path.Combine(root, "Mocks", "user-display-names.json");
        if (!File.Exists(namesPath))
            throw new FileNotFoundException($"Mock file not found: {namesPath}");

        var reelsRaw = File.ReadAllText(reelsPath).Trim();
        using var namesDoc = JsonDocument.Parse(File.ReadAllText(namesPath));
        var profileNames = namesDoc.RootElement.GetProperty("profileDisplayNames").GetRawText();

        var m = market.RootElement.GetRawText();
        var json = $"{{\"market\":{m},\"reels\":{reelsRaw},\"profileDisplayNames\":{profileNames}}}";
        return JsonDocument.Parse(json);
    }
}
