using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market.Utils;
using VibeTrade.Backend.Features.Recommendations;
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

        var author = new OfferQaAuthorSnapshot
        {
            Id = askedById,
            Name = askedByName,
            TrustScore = trustScore,
        };

        var newItem = new OfferQaComment
        {
            Id = qaId,
            Text = text,
            Question = text,
            ParentId = pid,
            AskedBy = author,
            Author = author,
            CreatedAt = createdMs,
        };

        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(offerId))
        {
            var emergent = await db.EmergentOffers.FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
            if (emergent is null || emergent.RetractedAtUtc is not null)
                return null;
            var eList = emergent.OfferQa.ToList();
            if (pid is not null && !eList.Any(x => string.Equals(x.Id, pid, StringComparison.Ordinal)))
                throw new ArgumentException("parentId no corresponde a un comentario de esta oferta.", nameof(parentId));
            eList.Insert(0, newItem);
            emergent.OfferQa = eList;
            await db.SaveChangesAsync(cancellationToken);
            return JsonSerializer.SerializeToNode(newItem, OfferQaJson.SerializerOptions) as JsonObject;
        }

        var product = await db.StoreProducts.FindAsync([offerId], cancellationToken);
        if (product is not null)
        {
            var list = product.OfferQa.ToList();
            if (pid is not null && !list.Any(x => string.Equals(x.Id, pid, StringComparison.Ordinal)))
                throw new ArgumentException("parentId no corresponde a un comentario de esta oferta.", nameof(parentId));
            list.Insert(0, newItem);
            product.OfferQa = list;
            product.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return JsonSerializer.SerializeToNode(newItem, OfferQaJson.SerializerOptions) as JsonObject;
        }

        var service = await db.StoreServices.FindAsync([offerId], cancellationToken);
        if (service is not null)
        {
            var list = service.OfferQa.ToList();
            if (pid is not null && !list.Any(x => string.Equals(x.Id, pid, StringComparison.Ordinal)))
                throw new ArgumentException("parentId no corresponde a un comentario de esta oferta.", nameof(parentId));
            list.Insert(0, newItem);
            service.OfferQa = list;
            service.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return JsonSerializer.SerializeToNode(newItem, OfferQaJson.SerializerOptions) as JsonObject;
        }

        return null;
    }

    public async Task<IReadOnlyList<OfferQaComment>?> GetOfferQaForOfferAsync(
        string offerId,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return null;

        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(oid))
        {
            var emergent = await db.EmergentOffers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid && x.RetractedAtUtc == null, cancellationToken);
            return emergent?.OfferQa;
        }

        var product = await db.StoreProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        if (product is not null)
            return product.OfferQa;

        var service = await db.StoreServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
        return service?.OfferQa;
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

        IReadOnlyList<OfferQaComment>? list = null;
        if (RecommendationBatchOfferLoader.IsEmergentPublicationId(oid))
        {
            var emergent = await db.EmergentOffers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid && x.RetractedAtUtc == null, cancellationToken);
            list = emergent?.OfferQa;
        }
        else
        {
            var product = await db.StoreProducts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
            list = product?.OfferQa;
            if (list is null || list.Count == 0)
            {
                var service = await db.StoreServices.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == oid, cancellationToken);
                list = service?.OfferQa;
            }
        }

        if (list is null)
            return null;

        foreach (var o in list)
        {
            if (!string.Equals(o.Id, cid, StringComparison.Ordinal))
                continue;
            var a = o.Author?.Id?.Trim();
            if (!string.IsNullOrEmpty(a))
                return a;
            var b = o.AskedBy?.Id?.Trim();
            if (!string.IsNullOrEmpty(b))
                return b;
        }

        return null;
    }
}
