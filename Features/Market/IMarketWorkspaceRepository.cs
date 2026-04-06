using System.Text.Json;

namespace VibeTrade.Backend.Features.Market;

public interface IMarketWorkspaceRepository
{
    Task<JsonDocument?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default);
}
