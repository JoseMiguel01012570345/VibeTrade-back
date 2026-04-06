using System.Text.Json;

namespace VibeTrade.Backend.Features.Bootstrap;

public interface IBootstrapService
{
    Task<JsonDocument> GetBootstrapAsync(CancellationToken cancellationToken = default);
}
