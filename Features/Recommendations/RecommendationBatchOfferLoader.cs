using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market.Utils;

namespace VibeTrade.Backend.Features.Recommendations;

/// <summary>
/// Carga candidatos y JSON de ofertas para lotes de recomendación. Las publicaciones emergentes de hoja de ruta
/// usan <see cref="EmergentOfferRow.Id" /> (<c>emo_*</c>), no el id de producto/servicio del hilo.
/// </summary>
internal static class RecommendationBatchOfferLoader
{
    public const string EmergentPublicationIdPrefix = "emo_";

    public static bool IsEmergentPublicationId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith(EmergentPublicationIdPrefix, StringComparison.Ordinal);

    public static async Task<Dictionary<string, OfferCandidate>> LoadOfferCandidatesAsync(
        AppDbContext db,
        string viewerUserId,
        IReadOnlyCollection<string> offerIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, OfferCandidate>(StringComparer.Ordinal);
        if (offerIds.Count == 0)
            return map;

        var idList = offerIds.ToList();
        var catalogIds = idList.Where(id => !IsEmergentPublicationId(id)).ToList();
        var emergentIds = idList.Where(IsEmergentPublicationId).ToList();

        if (catalogIds.Count > 0)
        {
            var products = await db.StoreProducts.AsNoTracking()
                .Include(p => p.Store)
                .Where(p => catalogIds.Contains(p.Id) && p.Published && p.Store.OwnerUserId != viewerUserId)
                .ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking()
                .Include(s => s.Store)
                .Where(s => catalogIds.Contains(s.Id) && (s.Published == null || s.Published == true) && s.Store.OwnerUserId != viewerUserId)
                .ToListAsync(cancellationToken);
            foreach (var item in products)
                map[item.Id] = CandidateFromProduct(item);
            foreach (var item in services)
                map[item.Id] = CandidateFromService(item);
        }

        if (emergentIds.Count == 0)
            return map;

        var emRows = await db.EmergentOffers.AsNoTracking()
            .Where(e => emergentIds.Contains(e.Id) && e.RetractedAtUtc == null && e.PublisherUserId != viewerUserId)
            .ToListAsync(cancellationToken);
        if (emRows.Count == 0)
            return map;

        var baseOfferIds = emRows.Select(e => e.OfferId).Distinct(StringComparer.Ordinal).ToList();
        var baseProducts = await db.StoreProducts.AsNoTracking()
            .Include(p => p.Store)
            .Where(p => baseOfferIds.Contains(p.Id) && p.Published)
            .ToListAsync(cancellationToken);
        var baseServices = await db.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .Where(s => baseOfferIds.Contains(s.Id) && (s.Published == null || s.Published == true))
            .ToListAsync(cancellationToken);
        var byP = baseProducts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var byS = baseServices.ToDictionary(s => s.Id, StringComparer.Ordinal);

        foreach (var e in emRows)
        {
            if (byP.TryGetValue(e.OfferId, out var p))
            {
                if (p.Store.OwnerUserId == viewerUserId)
                    continue;
                map[e.Id] = CandidateFromProduct(p, e.Id, e.OfferQa.Count);
            }
            else if (byS.TryGetValue(e.OfferId, out var s))
            {
                if (s.Store.OwnerUserId == viewerUserId)
                    continue;
                map[e.Id] = CandidateFromService(s, e.Id, e.OfferQa.Count);
            }
        }

        return map;
    }

    private static OfferCandidate CandidateFromProduct(StoreProductRow item) =>
        new(
            item.Id,
            item.StoreId,
            item.Category ?? "",
            item.Store.OwnerUserId,
            item.Store.TrustScore,
            item.UpdatedAt,
            item.OfferQa.Count,
            item.PopularityWeight);

    private static OfferCandidate CandidateFromService(StoreServiceRow item) =>
        new(
            item.Id,
            item.StoreId,
            item.Category ?? "",
            item.Store.OwnerUserId,
            item.Store.TrustScore,
            item.UpdatedAt,
            item.OfferQa.Count,
            item.PopularityWeight);

    private static OfferCandidate CandidateFromProduct(StoreProductRow item, string publicationId, int inquiryCount) =>
        new(
            publicationId,
            item.StoreId,
            item.Category ?? "",
            item.Store.OwnerUserId,
            item.Store.TrustScore,
            item.UpdatedAt,
            inquiryCount,
            item.PopularityWeight);

    private static OfferCandidate CandidateFromService(StoreServiceRow item, string publicationId, int inquiryCount) =>
        new(
            publicationId,
            item.StoreId,
            item.Category ?? "",
            item.Store.OwnerUserId,
            item.Store.TrustScore,
            item.UpdatedAt,
            inquiryCount,
            item.PopularityWeight);

    public static async Task<JsonObject> BuildOffersJsonInOrderAsync(
        AppDbContext db,
        IReadOnlyList<string> idsInOrder,
        CancellationToken cancellationToken)
    {
        if (idsInOrder.Count == 0)
            return new JsonObject();

        var offers = new JsonObject();
        var emergentIds = idsInOrder
            .Where(IsEmergentPublicationId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var catalogSet = idsInOrder
            .Where(id => !IsEmergentPublicationId(id))
            .ToHashSet(StringComparer.Ordinal);

        var byCatalog = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (catalogSet.Count > 0)
        {
            var products = await db.StoreProducts.AsNoTracking()
                .Where(p => catalogSet.Contains(p.Id))
                .ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking()
                .Where(s => catalogSet.Contains(s.Id))
                .ToListAsync(cancellationToken);
            foreach (var p in products)
                byCatalog[p.Id] = MarketCatalogOfferJsonBuilder.ProductRowToOfferJson(p);
            foreach (var s in services)
                byCatalog[s.Id] = MarketCatalogOfferJsonBuilder.ServiceRowToOfferJson(s);
        }

        if (emergentIds.Count == 0)
        {
            foreach (var id in idsInOrder)
            {
                if (byCatalog.TryGetValue(id, out var node))
                    offers[id] = node;
            }
            return offers;
        }

        var emRows = await db.EmergentOffers.AsNoTracking()
            .Where(e => emergentIds.Contains(e.Id) && e.RetractedAtUtc == null)
            .ToListAsync(cancellationToken);
        var emById = emRows.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var baseIds = emRows.Select(e => e.OfferId).Distinct(StringComparer.Ordinal).ToList();
        var baseP = await db.StoreProducts.AsNoTracking()
            .Where(p => baseIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var baseS = await db.StoreServices.AsNoTracking()
            .Where(s => baseIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        var byP = baseP.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var byS = baseS.ToDictionary(s => s.Id, StringComparer.Ordinal);

        foreach (var id in idsInOrder)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (emById.TryGetValue(id, out var em))
            {
                if (byP.TryGetValue(em.OfferId, out var p))
                    offers[id] = ToEmergentRoutePublicationJson(em, p, null, null);
                else if (byS.TryGetValue(em.OfferId, out var s))
                    offers[id] = ToEmergentRoutePublicationJson(em, null, s, null);
                else
                {
                    var fallbackStoreId = await TryResolveStoreIdForEmergentOrphanAsync(db, em, cancellationToken);
                    offers[id] = ToEmergentRoutePublicationJson(em, null, null, fallbackStoreId);
                }

                continue;
            }
            if (byCatalog.TryGetValue(id, out var node))
                offers[id] = node;
        }

        return offers;
    }

    private static async Task<string?> TryResolveStoreIdForEmergentOrphanAsync(
        AppDbContext db,
        EmergentOfferRow em,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(em.PublisherUserId))
            return null;
        return await db.Stores.AsNoTracking()
            .Where(x => x.OwnerUserId == em.PublisherUserId)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static JsonObject ToEmergentRoutePublicationJson(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId)
    {
        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        var storeIdForOrphan = (fallbackStoreId ?? "").Trim();
        var baseNode = p is not null
            ? MarketCatalogOfferJsonBuilder.ProductRowToOfferJson(p)
            : s is not null
                ? MarketCatalogOfferJsonBuilder.ServiceRowToOfferJson(s)
                : new JsonObject
                {
                    ["id"] = e.Id,
                    ["title"] = snap.Titulo,
                    ["storeId"] = string.IsNullOrEmpty(storeIdForOrphan) ? "" : storeIdForOrphan,
                    ["price"] = "—",
                };

        baseNode["id"] = e.Id;
        baseNode["emergentBaseOfferId"] = e.OfferId;
        baseNode["emergentThreadId"] = e.ThreadId;
        baseNode["emergentRouteSheetId"] = e.RouteSheetId;
        baseNode["isEmergentRoutePublication"] = true;
        if (!string.IsNullOrWhiteSpace(snap.MonedaPago))
            baseNode["emergentMonedaPago"] = snap.MonedaPago.Trim();
        if (!string.IsNullOrWhiteSpace(snap.Titulo))
            baseNode["title"] = snap.Titulo.Trim();
        var routeLine = RouteSummaryLine(snap);
        var prevDesc = baseNode["description"]?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(routeLine))
        {
            baseNode["description"] = string.IsNullOrWhiteSpace(prevDesc)
                ? routeLine
                : routeLine + "\n\n" + prevDesc;
        }
        if (baseNode["tags"] is JsonArray tArr)
            tArr.Add("Hoja de ruta (publicada)");
        else
            baseNode["tags"] = new JsonArray { "Hoja de ruta (publicada)" };

        var paradasSnap = snap.Paradas ?? [];
        if (paradasSnap.Count > 0)
        {
            var paradasNode = new JsonArray();
            foreach (var leg in paradasSnap)
            {
                var legNode = new JsonObject
                {
                    ["origen"] = leg.Origen?.Trim() ?? "",
                    ["destino"] = leg.Destino?.Trim() ?? ""
                };
                if (!string.IsNullOrWhiteSpace(leg.OrigenLat)) legNode["origenLat"] = leg.OrigenLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.OrigenLng)) legNode["origenLng"] = leg.OrigenLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLat)) legNode["destinoLat"] = leg.DestinoLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLng)) legNode["destinoLng"] = leg.DestinoLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.MonedaPago)) legNode["monedaPago"] = leg.MonedaPago.Trim();
                paradasNode.Add(legNode);
            }
            baseNode["emergentRouteParadas"] = paradasNode;
        }

        baseNode["qa"] = OfferQaJson.ToJsonNode(e.OfferQa);
        return baseNode;
    }

    private static string RouteSummaryLine(EmergentRouteSheetSnapshot snap)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.MercanciasResumen))
            parts.Add(snap.MercanciasResumen.Trim());
        foreach (var leg in snap.Paradas ?? [])
        {
            if (!string.IsNullOrWhiteSpace(leg.Origen) && !string.IsNullOrWhiteSpace(leg.Destino))
                parts.Add($"{leg.Origen} → {leg.Destino}");
        }
        return string.Join(" · ", parts);
    }
}
