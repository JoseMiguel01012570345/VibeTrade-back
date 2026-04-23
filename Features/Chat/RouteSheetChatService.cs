using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteSheetChatService(AppDbContext db) : IRouteSheetChatService
{
    public const string EmergentKindRouteSheet = EmergentRouteOfferRanking.EmergentKindRouteSheet;

    public async Task<IReadOnlyList<RouteSheetPayload>?> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(userId, t))
            return null;

        return await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == threadId && x.DeletedAtUtc == null)
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
        return true;
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
        return true;
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
