using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeTrade.Backend.Features.Market;

/// <summary>Sincroniza tiendas, productos y servicios entre PostgreSQL y el shape JSON del cliente.</summary>
public interface IMarketCatalogSyncService
{
    /// <summary>Solo metadatos de tienda (ficha de perfil): sin productos/servicios ni pitch.</summary>
    Task ApplyStoreProfilesFromWorkspaceAsync(JsonElement workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Solo catálogo (productos/servicios y pitch) para las tiendas indicadas en el JSON.</summary>
    Task ApplyStoreCatalogsFromWorkspaceAsync(JsonElement workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Solo <c>offers[*].qa</c> persistido en filas de producto/servicio.</summary>
    Task ApplyOfferInquiriesFromWorkspaceAsync(JsonElement workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>Añade un comentario (estilo reels: <c>parentId</c> opcional) al array <c>OfferQaJson</c>.</summary>
    /// <returns>El ítem creado, o <c>null</c> si no existe producto/servicio con ese id.</returns>
    Task<JsonObject?> AppendOfferInquiryAsync(
        string offerId,
        string text,
        string? parentId,
        string askedById,
        string askedByName,
        int trustScore,
        long? createdAtMs,
        CancellationToken cancellationToken = default);

    /// <summary>Autor del comentario con <paramref name="commentId"/> en la oferta (producto/servicio).</summary>
    Task<string?> TryGetOfferCommentAuthorIdAsync(string offerId, string commentId, CancellationToken cancellationToken = default);

    /// <summary>JSON array <c>OfferQaJson</c> tal cual en BD, o null si no existe el producto/servicio.</summary>
    Task<string?> GetOfferQaJsonForOfferAsync(string offerId, CancellationToken cancellationToken = default);

    Task<JsonObject> BuildStoresJsonObjectAsync(CancellationToken cancellationToken = default);

    Task<JsonObject> BuildStoreCatalogsJsonObjectAsync(CancellationToken cancellationToken = default);

    /// <summary><c>{"store":...,"catalog":...}</c> o null si no existe la tienda.</summary>
    Task<JsonDocument?> GetStoreDetailDocumentAsync(string storeId, CancellationToken cancellationToken = default);

    /// <summary>Upsert de un solo producto; cuerpo = ficha de producto (mismo shape que en catálogo).</summary>
    Task<StoreCatalogUpsertResult> UpsertStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        JsonElement product,
        CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> DeleteStoreProductAsync(
        string storeId,
        string productId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>Upsert de un solo servicio; cuerpo = ficha de servicio.</summary>
    Task<StoreCatalogUpsertResult> UpsertStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        JsonElement service,
        CancellationToken cancellationToken = default);

    Task<StoreCatalogUpsertResult> DeleteStoreServiceAsync(
        string storeId,
        string serviceId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Feed de ofertas para el Home: una entrada por producto/servicio publicado (shape alineado al tipo Offer del cliente).
    /// </summary>
    Task<(JsonObject Offers, JsonArray OfferIds)> BuildPublishedOffersFeedAsync(CancellationToken cancellationToken = default);
}
