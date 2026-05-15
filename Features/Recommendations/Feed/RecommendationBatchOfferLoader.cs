using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations.Dtos;

namespace VibeTrade.Backend.Features.Recommendations.Feed;

/// <summary>
/// Carga candidatos y JSON de ofertas para lotes de recomendación. Las publicaciones emergentes de hoja de ruta
/// usan <see cref="EmergentOfferRow.Id" /> (prefijo <see cref="OfferUtils.EmergentPublicationIdPrefix" />), no el id de producto/servicio del hilo.
/// </summary>
internal static class RecommendationBatchOfferLoader
{
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
        var catalogIds = idList.Where(id => !OfferUtils.IsEmergentPublicationId(id)).ToList();
        var emergentIds = idList.Where(OfferUtils.IsEmergentPublicationId).ToList();

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
            .Where(e => emergentIds.Contains(e.Id)
                && e.RetractedAtUtc == null
                && e.PublisherUserId != viewerUserId
                && db.ChatRouteSheets.Any(r =>
                    r.ThreadId == e.ThreadId
                    && r.RouteSheetId == e.RouteSheetId
                    && r.DeletedAtUtc == null
                    && r.PublishedToPlatform))
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

    public static async Task<Dictionary<string, HomeOfferViewDto>> BuildOffersViewInOrderAsync(
        AppDbContext db,
        IOfferService offerService,
        IReadOnlyList<string> idsInOrder,
        CancellationToken cancellationToken)
    {
        if (idsInOrder.Count == 0)
            return new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal);

        var offers = new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal);
        var emergentIds = idsInOrder
            .Where(OfferUtils.IsEmergentPublicationId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var catalogSet = idsInOrder
            .Where(id => !OfferUtils.IsEmergentPublicationId(id))
            .ToHashSet(StringComparer.Ordinal);

        var byCatalog = new Dictionary<string, HomeOfferViewDto>(StringComparer.Ordinal);
        if (catalogSet.Count > 0)
        {
            var products = await db.StoreProducts.AsNoTracking()
                .Where(p => catalogSet.Contains(p.Id))
                .ToListAsync(cancellationToken);
            var services = await db.StoreServices.AsNoTracking()
                .Where(s => catalogSet.Contains(s.Id))
                .ToListAsync(cancellationToken);
            foreach (var p in products)
                byCatalog[p.Id] = offerService.FromProductRow(p);
            foreach (var s in services)
                byCatalog[s.Id] = offerService.FromServiceRow(s);
        }

        if (emergentIds.Count == 0)
        {
            foreach (var id in idsInOrder)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (byCatalog.TryGetValue(id, out var node))
                    offers[id] = node;
            }

            return offers;
        }

        var emRows = await db.EmergentOffers.AsNoTracking()
            .Where(e => emergentIds.Contains(e.Id)
                && e.RetractedAtUtc == null
                && db.ChatRouteSheets.Any(r =>
                    r.ThreadId == e.ThreadId
                    && r.RouteSheetId == e.RouteSheetId
                    && r.DeletedAtUtc == null
                    && r.PublishedToPlatform))
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
                HomeOfferViewDto emergentNode;
                if (byP.TryGetValue(em.OfferId, out var p))
                    emergentNode = offerService.CreateEmergentRoutePublication(em, p, null, null);
                else if (byS.TryGetValue(em.OfferId, out var s))
                    emergentNode = offerService.CreateEmergentRoutePublication(em, null, s, null);
                else
                {
                    var fallbackStoreId = await TryResolveStoreIdForEmergentOrphanAsync(db, em, cancellationToken);
                    emergentNode = offerService.CreateEmergentRoutePublication(em, null, null, fallbackStoreId);
                }

                await EnrichEmergentParadasViewFromLiveSheetAsync(db, em, emergentNode, cancellationToken);
                offers[id] = emergentNode;
                continue;
            }

            if (byCatalog.TryGetValue(id, out var catalogNode))
                offers[id] = catalogNode;
        }

        return offers;
    }

    private static async Task EnrichEmergentParadasViewFromLiveSheetAsync(
        AppDbContext db,
        EmergentOfferRow em,
        HomeOfferViewDto offer,
        CancellationToken cancellationToken)
    {
        if (offer.EmergentRouteParadas is not { Count: > 0 })
            return;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == em.ThreadId
                    && x.RouteSheetId == em.RouteSheetId
                    && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null)
            return;

        OfferUtils.ApplyLiveParadaStopIds(offer, sheetRow.Payload);
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

    /// <summary>Clave estable para emparejar filas de <see cref="ChatRouteSheetRow"/> con una publicación emergente.</summary>
    public static string EmergentOfferRouteSheetKey(string threadId, string routeSheetId) =>
        OfferUtils.EmergentOfferRouteSheetKey(threadId, routeSheetId);

    /// <summary>Carga hojas de ruta vivas (chat) para enriquecer tramos en búsqueda / feed sin N consultas por oferta.</summary>
    public static async Task<Dictionary<string, RouteSheetPayload>> LoadLiveRouteSheetsForEmergentsAsync(
        AppDbContext db,
        IReadOnlyCollection<EmergentOfferRow> emergents,
        CancellationToken cancellationToken)
    {
        if (emergents.Count == 0)
            return new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal);

        var wanted = emergents
            .Select(em => (ThreadId: (em.ThreadId ?? "").Trim(), RouteSheetId: (em.RouteSheetId ?? "").Trim()))
            .Where(p => p.ThreadId.Length > 0 && p.RouteSheetId.Length > 0)
            .ToHashSet();
        if (wanted.Count == 0)
            return new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal);

        var threadIds = wanted.Select(p => p.ThreadId).Distinct().ToList();
        var rows = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.DeletedAtUtc == null && threadIds.Contains(x.ThreadId))
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var tid = (r.ThreadId ?? "").Trim();
            var sid = (r.RouteSheetId ?? "").Trim();
            if (!wanted.Contains((tid, sid)))
                continue;
            var k = OfferUtils.EmergentOfferRouteSheetKey(tid, sid);
            if (!map.ContainsKey(k))
                map[k] = r.Payload;
        }

        return map;
    }
}
