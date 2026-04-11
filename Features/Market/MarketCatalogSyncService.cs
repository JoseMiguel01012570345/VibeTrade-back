using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Market;

public sealed partial class MarketCatalogSyncService(AppDbContext db) : IMarketCatalogSyncService
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
        string question,
        string askedById,
        string askedByName,
        int trustScore,
        long? createdAtMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(offerId))
            throw new ArgumentException("offerId is required.", nameof(offerId));

        var now = DateTimeOffset.UtcNow;
        var qaId = $"qa_{Guid.NewGuid():N}";
        var createdMs = createdAtMs is long ms && ms > 0 && ms < 4_102_441_920_000L
            ? ms
            : now.ToUnixTimeMilliseconds();

        var newItem = new JsonObject
        {
            ["id"] = qaId,
            ["question"] = question,
            ["askedBy"] = new JsonObject
            {
                ["id"] = askedById,
                ["name"] = askedByName,
                ["trustScore"] = trustScore,
            },
            ["createdAt"] = createdMs,
        };

        var product = await db.StoreProducts.FindAsync([offerId], cancellationToken);
        if (product is not null)
        {
            var arr = MarketCatalogJsonHelpers.ParseOfferQaArray(product.OfferQaJson);
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
            arr.Insert(0, newItem);
            service.OfferQaJson = arr.ToJsonString();
            service.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return newItem;
        }

        return null;
    }
}
