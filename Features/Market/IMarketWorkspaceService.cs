using System.Text.Json;

namespace VibeTrade.Backend.Features.Market;

public interface IMarketWorkspaceService
{
    Task<JsonDocument> GetOrSeedAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonDocument document, CancellationToken cancellationToken = default);

    /// <summary>Devuelve <c>store</c> + <c>catalog</c> del workspace, o <c>null</c> si no existe la tienda.</summary>
    Task<JsonDocument?> GetStoreDetailAsync(string storeId, CancellationToken cancellationToken = default);
}
