using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Sincroniza tiendas, productos y servicios entre PostgreSQL y el shape JSON del cliente.</summary>
public interface IMarketCatalogSyncService
{
    /// <summary>Persiste <c>stores</c> y <c>storeCatalogs</c> del workspace en tablas relacionales.</summary>
    Task ApplyStoresAndCatalogsFromWorkspaceAsync(JsonElement workspaceRoot, CancellationToken cancellationToken = default);

    Task<JsonObject> BuildStoresJsonObjectAsync(CancellationToken cancellationToken = default);

    Task<JsonObject> BuildStoreCatalogsJsonObjectAsync(CancellationToken cancellationToken = default);

    /// <summary><c>{"store":...,"catalog":...}</c> o null si no existe la tienda.</summary>
    Task<JsonDocument?> GetStoreDetailDocumentAsync(string storeId, CancellationToken cancellationToken = default);
}
