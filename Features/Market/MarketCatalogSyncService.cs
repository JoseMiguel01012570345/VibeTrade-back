using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Search;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService(
    AppDbContext db,
    IStoreSearchIndexWriter storeSearchIndex,
    IChatService chat) : IMarketCatalogSyncService
{
    public Task ApplyStoreProfilesFromWorkspaceAsync(
        JsonElement workspaceRoot,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(workspaceRoot, storeProfiles: true, catalogs: false, offerQa: false, cancellationToken);

    public Task ApplyStoreCatalogsFromWorkspaceAsync(
        JsonElement workspaceRoot,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(workspaceRoot, storeProfiles: false, catalogs: true, offerQa: false, cancellationToken);

    public Task ApplyOfferInquiriesFromWorkspaceAsync(
        JsonElement workspaceRoot,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(workspaceRoot, storeProfiles: false, catalogs: false, offerQa: true, cancellationToken);

    public async Task<JsonObject?> AppendOfferInquiryAsync(
        string offerId,
        string text,
        string? parentId,
        string askedById,
        string askedByName,
        int trustScore,
        long? createdAtMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(offerId))
            throw new ArgumentException("offerId is required.", nameof(offerId));

        var pid = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();

        var now = DateTimeOffset.UtcNow;
        var qaId = $"qa_{Guid.NewGuid():N}";
        var createdMs = createdAtMs is long ms && ms > 0 && ms < 4_102_441_920_000L
            ? ms
            : now.ToUnixTimeMilliseconds();

        // Dos objetos distintos: JsonNode no permite compartir la misma instancia entre propiedades (un solo padre).
        JsonObject BuildAuthorSnapshot() => new JsonObject
        {
            ["id"] = askedById,
            ["name"] = askedByName,
            ["trustScore"] = trustScore,
        };

        var newItem = new JsonObject
        {
            ["id"] = qaId,
            ["text"] = text,
            ["question"] = text,
            ["parentId"] = pid,
            ["askedBy"] = BuildAuthorSnapshot(),
            ["author"] = BuildAuthorSnapshot(),
            ["createdAt"] = createdMs,
        };

        var product = await db.StoreProducts.FindAsync([offerId], cancellationToken);
        if (product is not null)
        {
            var arr = MarketCatalogJsonHelpers.ParseOfferQaArray(product.OfferQaJson);
            if (pid is not null && !CommentIdExistsInArray(arr, pid))
                throw new ArgumentException("parentId no corresponde a un comentario de esta oferta.", nameof(parentId));
            arr.Insert(0, newItem);
            product.OfferQaJson = arr.ToJsonString();
            product.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return newItem;
        }

        var service = await db.StoreServices.FindAsync([offerId], cancellationToken);
        if (service is not null)
        {
            var arr = MarketCatalogJsonHelpers.ParseOfferQaArray(service.OfferQaJson);
            if (pid is not null && !CommentIdExistsInArray(arr, pid))
                throw new ArgumentException("parentId no corresponde a un comentario de esta oferta.", nameof(parentId));
            arr.Insert(0, newItem);
            service.OfferQaJson = arr.ToJsonString();
            service.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return newItem;
        }

        return null;
    }

    public async Task<string?> GetOfferQaJsonForOfferAsync(
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return null;

        var product = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        if (product is not null)
            return string.IsNullOrWhiteSpace(product.OfferQaJson) ? "[]" : product.OfferQaJson;

        var service = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        if (service is null)
            return null;
        return string.IsNullOrWhiteSpace(service.OfferQaJson) ? "[]" : service.OfferQaJson;
    }

    public async Task<string?> TryGetOfferCommentAuthorIdAsync(
        string offerId,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        var cid = (commentId ?? "").Trim();
        if (oid.Length < 2 || cid.Length < 2)
            return null;

        var product = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        var json = product?.OfferQaJson;
        if (json is null)
        {
            var service = await db.StoreServices.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
            json = service?.OfferQaJson;
        }

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var arr = JsonNode.Parse(json) as JsonArray;
            if (arr is null)
                return null;
            foreach (var node in arr)
            {
                if (node is not JsonObject o)
                    continue;
                if (!string.Equals(o["id"]?.GetValue<string>(), cid, StringComparison.Ordinal))
                    continue;
                if (o["author"] is JsonObject auth && auth["id"]?.GetValue<string>() is { } a)
                    return a;
                if (o["askedBy"] is JsonObject ask && ask["id"]?.GetValue<string>() is { } b)
                    return b;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool CommentIdExistsInArray(JsonArray arr, string commentId)
    {
        foreach (var node in arr)
        {
            if (node is JsonObject o && string.Equals(o["id"]?.GetValue<string>(), commentId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
