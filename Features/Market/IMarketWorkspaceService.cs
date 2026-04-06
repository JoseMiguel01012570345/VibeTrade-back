using System.Text.Json;

namespace VibeTrade.Backend.Features.Market;

public interface IMarketWorkspaceService
{
    Task<JsonDocument> GetOrSeedAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default);
}
