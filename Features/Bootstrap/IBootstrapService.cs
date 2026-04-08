using System.Text.Json;

namespace VibeTrade.Backend.Features.Bootstrap;

public interface IBootstrapService
{
    Task<JsonDocument> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default);
}
