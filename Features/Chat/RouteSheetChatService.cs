using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Search;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteSheetChatService(
    AppDbContext db,
    IStoreSearchIndexWriter storeSearchIndex,
    IChatService chat) : IRouteSheetChatService
{
    public const string EmergentKindRouteSheet = EmergentRouteOfferRanking.EmergentKindRouteSheet;

    public async Task<IReadOnlyList<RouteSheetPayload>?> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return null;

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || !await chat.UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return null;

        return await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null)
            .OrderBy(x => x.RouteSheetId)
            .Select(x => x.Payload)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(userId, t))
            return false;

        var rsId = (routeSheetId ?? "").Trim();
        if (rsId.Length == 0)
            return false;

        var idInPayload = (payload.Id ?? "").Trim();
        if (idInPayload.Length > 0 && !string.Equals(idInPayload, rsId, StringComparison.Ordinal))
            return false;

        payload.Id = rsId;
        payload.ThreadId = threadId;
        payload.Paradas ??= new List<RouteStopPayload>();

        var published = payload.PublicadaPlataforma == true;

        var row = await db.ChatRouteSheets.FirstOrDefaultAsync(
            x => x.ThreadId == threadId && x.RouteSheetId == rsId,
            cancellationToken);
        var wasExistingSheet = row is not null;
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.ChatRouteSheets.Add(new ChatRouteSheetRow
            {
                ThreadId = threadId,
                RouteSheetId = rsId,
                Payload = payload,
                PublishedToPlatform = published,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            row.Payload = payload;
            row.PublishedToPlatform = published;
            row.UpdatedAtUtc = now;
            if (row.DeletedAtUtc is not null)
            {
                row.DeletedAtUtc = null;
                row.DeletedByUserId = null;
            }
        }

        await SyncEmergentOfferAsync(t, rsId, userId, published, payload, cancellationToken);
        if (published)
            await EnsureTradeAgreementLinkForPublishedRouteAsync(threadId, rsId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (wasExistingSheet)
        {
            var notice = BuildRouteSheetEditedNoticeText(payload);
            await chat.PostSystemThreadNoticeAsync(userId.Trim(), threadId, notice, cancellationToken);
        }

        await ReindexCatalogStoresForThreadOfferAsync(userId, t.OfferId, cancellationToken);
        return true;
    }

    private static string BuildRouteSheetEditedNoticeText(RouteSheetPayload payload)
    {
        var title = (payload.Titulo ?? "").Trim();
        if (title.Length > 120)
            title = title[..120] + "…";
        return title.Length > 0
            ? $"Se actualizó la hoja de ruta «{title}»."
            : "Se actualizó la hoja de ruta.";
    }

    /// <summary>
    /// Al publicar, el vínculo acuerdo↔hoja vive en <c>TradeAgreementRow.RouteSheetId</c>.
    /// El flujo de cliente exige hoja vinculada en estado local, pero el PUT de hoja no actualizaba el acuerdo en BD
    /// si faltó el PATCH; con un solo acuerdo en el hilo, lo persistimos aquí.
    /// </summary>
    private async Task EnsureTradeAgreementLinkForPublishedRouteAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var agreements = await db.TradeAgreements
            .Where(a => a.ThreadId == threadId && a.DeletedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (agreements.Count != 1)
            return;
        var ag = agreements[0];
        if (string.Equals(ag.RouteSheetId?.Trim(), routeSheetId, StringComparison.Ordinal))
            return;
        ag.RouteSheetId = routeSheetId;
    }

    public async Task<bool> DeleteAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(userId, t))
            return false;

        var rsId = (routeSheetId ?? "").Trim();
        if (rsId.Length == 0)
            return false;

        var row = await db.ChatRouteSheets.FirstOrDefaultAsync(
            x => x.ThreadId == threadId && x.RouteSheetId == rsId,
            cancellationToken);
        if (row is null)
            return false;

        if (row.DeletedAtUtc is not null)
            return true;

        var retractNow = DateTimeOffset.UtcNow;
        row.DeletedAtUtc = retractNow;
        row.DeletedByUserId = userId.Trim();
        row.PublishedToPlatform = false;
        var p = row.Payload;
        p.PublicadaPlataforma = false;
        row.Payload = p;
        await RetractEmergentAsync(threadId, rsId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await ReindexCatalogStoresForThreadOfferAsync(userId, t.OfferId, cancellationToken);
        return true;
    }

    private async Task ReindexCatalogStoresForThreadOfferAsync(
        string publisherUserId,
        string threadOfferId,
        CancellationToken cancellationToken)
    {
        var storeIds = new HashSet<string>(StringComparer.Ordinal);
        var offerId = (threadOfferId ?? "").Trim();
        if (offerId.Length > 0)
        {
            // Índice ES: las publicaciones emergentes se cuelgan del árbol de la tienda del ítem del hilo.
            // No exigir «published»: si no, caemos al fallback «primera tienda del publicador» y otra tienda
            // correcta nunca recibe el bulk → el documento <c>emo_*</c> no se indexa.
            var p = await db.StoreProducts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
            if (p is not null)
                storeIds.Add(p.StoreId);
            else
            {
                var sv = await db.StoreServices.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == offerId, cancellationToken);
                if (sv is not null)
                    storeIds.Add(sv.StoreId);
            }
        }

        if (storeIds.Count == 0)
        {
            var publisher = (publisherUserId ?? "").Trim();
            if (publisher.Length == 0)
                return;
            var orphanStore = await db.Stores.AsNoTracking()
                .Where(x => x.OwnerUserId == publisher)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(orphanStore))
                storeIds.Add(orphanStore);
        }

        if (storeIds.Count > 0)
            await storeSearchIndex.UpsertStoresAsync(storeIds.ToList(), cancellationToken);
    }

    private async Task SyncEmergentOfferAsync(
        ChatThreadRow thread,
        string routeSheetId,
        string publisherUserId,
        bool publishedToPlatform,
        RouteSheetPayload payload,
        CancellationToken cancellationToken)
    {
        if (!publishedToPlatform)
        {
            await RetractEmergentAsync(thread.Id, routeSheetId, cancellationToken);
            return;
        }

        var snap = EmergentRouteSheetSnapshot.FromRouteSheet(payload);
        var now = DateTimeOffset.UtcNow;
        var emergent = await db.EmergentOffers.FirstOrDefaultAsync(
            e => e.ThreadId == thread.Id && e.RouteSheetId == routeSheetId,
            cancellationToken);
        if (emergent is null)
        {
            db.EmergentOffers.Add(new EmergentOfferRow
            {
                Id = "emo_" + Guid.NewGuid().ToString("N"),
                Kind = EmergentKindRouteSheet,
                ThreadId = thread.Id,
                OfferId = thread.OfferId,
                RouteSheetId = routeSheetId,
                PublisherUserId = publisherUserId,
                RouteSheetSnapshot = snap,
                PublishedAtUtc = now,
                RetractedAtUtc = null,
            });
        }
        else
        {
            emergent.Kind = EmergentKindRouteSheet;
            emergent.OfferId = thread.OfferId;
            emergent.PublisherUserId = publisherUserId;
            emergent.RouteSheetSnapshot = snap;
            emergent.PublishedAtUtc = now;
            emergent.RetractedAtUtc = null;
        }
    }

    private async Task RetractEmergentAsync(string threadId, string routeSheetId, CancellationToken cancellationToken)
    {
        var emergent = await db.EmergentOffers.FirstOrDefaultAsync(
            e => e.ThreadId == threadId && e.RouteSheetId == routeSheetId,
            cancellationToken);
        if (emergent is null || emergent.RetractedAtUtc is not null)
            return;
        emergent.RetractedAtUtc = DateTimeOffset.UtcNow;
    }
}
