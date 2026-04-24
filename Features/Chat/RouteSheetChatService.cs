using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteSheetChatService(
    AppDbContext db,
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

        var row = await db.ChatRouteSheets.FirstOrDefaultAsync(
            x => x.ThreadId == threadId && x.RouteSheetId == rsId,
            cancellationToken);
        var wasExistingSheet = row is not null;
        RouteSheetPayload? oldSnapshot = null;
        if (row is not null)
        {
            oldSnapshot = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(row.Payload, RouteSheetJson.Options),
                RouteSheetJson.Options);
        }

        var merged = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(payload, RouteSheetJson.Options),
                RouteSheetJson.Options)
            ?? payload;
        merged.Id = rsId;
        merged.ThreadId = threadId;
        merged.Paradas ??= new List<RouteStopPayload>();

        RouteSheetEditAckPayload? nextAck = null;
        HashSet<string>? affectedForNotice = null;
        List<RouteTramoSubscriptionRow>? confirmedRowsForNotice = null;
        if (wasExistingSheet && oldSnapshot is not null)
        {
            var subs = await db.RouteTramoSubscriptions
                .Where(x => x.ThreadId == threadId && x.RouteSheetId == rsId)
                .ToListAsync(cancellationToken);
            var confirmedOnSheet = subs
                .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var confirmedIds = RouteSheetEditAckComputation.ConfirmedCarrierIdsForSheet(subs, rsId);
            var affected = RouteSheetEditAckComputation.AffectedConfirmedCarrierIds(oldSnapshot, merged, confirmedOnSheet);
            affectedForNotice = affected;
            confirmedRowsForNotice = confirmedOnSheet;
            nextAck = RouteSheetEditAckComputation.BuildNextEditAck(oldSnapshot.RouteSheetEditAck, confirmedIds, affected);
            if (nextAck is null
                && confirmedIds.Count > 0
                && affected.Count == 0
                && oldSnapshot.RouteSheetEditAck is not null)
                nextAck = oldSnapshot.RouteSheetEditAck;
            merged.RouteSheetEditAck = nextAck;
        }
        else
            merged.RouteSheetEditAck = null;

        var published = merged.PublicadaPlataforma == true;
        var now = DateTimeOffset.UtcNow;
        var persisted = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(merged, RouteSheetJson.Options),
                RouteSheetJson.Options)
            ?? merged;

        if (row is null)
        {
            db.ChatRouteSheets.Add(new ChatRouteSheetRow
            {
                ThreadId = threadId,
                RouteSheetId = rsId,
                Payload = persisted,
                PublishedToPlatform = published,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            row.Payload = persisted;
            row.PublishedToPlatform = published;
            row.UpdatedAtUtc = now;
            if (row.DeletedAtUtc is not null)
            {
                row.DeletedAtUtc = null;
                row.DeletedByUserId = null;
            }
        }

        await SyncEmergentOfferAsync(t, rsId, userId, published, persisted, cancellationToken);
        if (published)
            await EnsureTradeAgreementLinkForPublishedRouteAsync(threadId, rsId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (wasExistingSheet)
        {
            string notice;
            if (nextAck is not null
                && affectedForNotice is not null
                && affectedForNotice.Count > 0
                && confirmedRowsForNotice is not null)
            {
                var nameIds = affectedForNotice.ToList();
                var accounts = await db.UserAccounts.AsNoTracking()
                    .Where(x => nameIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.DisplayName ?? "", cancellationToken);
                notice = RouteSheetEditAckComputation.BuildEditNoticeText(
                    persisted.Titulo,
                    persisted,
                    affectedForNotice,
                    confirmedRowsForNotice,
                    accounts);
            }
            else
                notice = BuildRouteSheetEditedNoticeText(persisted);

            await chat.PostSystemThreadNoticeAsync(userId.Trim(), threadId, notice, cancellationToken);
        }

        if (wasExistingSheet
            && RouteSheetEditAckComputation.HasPendingCarrierAck(persisted.RouteSheetEditAck))
        {
            var emergentId = await EmergentPublicationIdForSheetAsync(threadId, rsId, cancellationToken);
            await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                threadId,
                rsId,
                "sheet_edit_pending",
                userId.Trim(),
                emergentId,
                cancellationToken);
        }

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
        var subs = await db.RouteTramoSubscriptions
            .Where(x =>
                x.ThreadId == threadId
                && x.RouteSheetId == rsId
                && x.Status != "withdrawn")
            .ToListAsync(cancellationToken);

        var nConfirmed = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.CarrierUserId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        foreach (var s in subs)
        {
            s.Status = "withdrawn";
            s.UpdatedAtUtc = retractNow;
        }

        if (nConfirmed > 0)
        {
            var storeId = (t.StoreId ?? "").Trim();
            if (storeId.Length >= 2)
            {
                var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
                if (storeRow is not null)
                {
                    storeRow.TrustScore = Math.Max(
                        -10_000,
                        storeRow.TrustScore
                            - RouteSheetEditAckComputation.StoreTrustPenaltyPerConfirmedCarrierOnSheetDelete
                                * nConfirmed);
                }
            }
        }

        row.DeletedAtUtc = retractNow;
        row.DeletedByUserId = userId.Trim();
        row.PublishedToPlatform = false;
        var p = row.Payload;
        p.PublicadaPlataforma = false;
        row.Payload = p;

        var emRow0 = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ThreadId == threadId && e.RouteSheetId == rsId && e.RetractedAtUtc == null,
                cancellationToken);
        var emergentPubId = string.IsNullOrWhiteSpace(emRow0?.Id) ? null : emRow0!.Id.Trim();

        await RetractEmergentAsync(threadId, rsId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var title = (row.Payload.Titulo ?? "").Trim();
        if (title.Length > 120)
            title = title[..120] + "…";
        var sys = title.Length > 0 ? $"Se eliminó la hoja de ruta «{title}»." : "Se eliminó una hoja de ruta.";
        if (subs.Count > 0)
            sys += " Los transportistas con tramo en la oferta salieron del chat.";
        if (nConfirmed > 0)
            sys += $" A la tienda se aplicó un ajuste de confianza por cada transportista confirmado ({nConfirmed}× demo).";
        await chat.PostSystemThreadNoticeAsync(userId.Trim(), threadId, sys, cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            threadId,
            rsId,
            "sheet_deleted",
            userId.Trim(),
            emergentPubId,
            cancellationToken);

        return true;
    }

    public async Task<bool> CarrierRespondToSheetEditAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        var cid = (carrierUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (cid.Length < 2 || tid.Length < 4 || rsid.Length < 1)
            return false;

        var thread = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return false;
        if (!await chat.UserCanAccessThreadRowAsync(cid, thread, cancellationToken))
            return false;

        var sheetRow = await db.ChatRouteSheets.FirstOrDefaultAsync(
            x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
            cancellationToken);
        if (sheetRow is null)
            return false;

        var ack = sheetRow.Payload.RouteSheetEditAck;
        if (ack?.ByCarrier is null)
            return false;
        var ackKey = ResolveCarrierKeyInEditAck(ack.ByCarrier, cid);
        if (ackKey is null
            || !string.Equals((ack.ByCarrier[ackKey] ?? "").Trim(), "pending", StringComparison.OrdinalIgnoreCase))
            return false;

        var subsConfirmed = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid)
            .ToListAsync(cancellationToken);
        if (!subsConfirmed.Any(x =>
                string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase)
                && ChatService.UserIdsMatchLoose(cid, x.CarrierUserId)))
            return false;

        var carrierName =
            (await db.UserAccounts.AsNoTracking()
                .Where(x => x.Id == cid)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(cancellationToken))?.Trim() is { Length: > 0 } dn
                ? dn
                : "Transportista";
        var sheetTitle = TruncateRouteSheetTitle(sheetRow.Payload.Titulo);

        var now = DateTimeOffset.UtcNow;
        ack.ByCarrier[ackKey] = accept ? "accepted" : "rejected";

        if (!accept)
        {
            var subs = await db.RouteTramoSubscriptions
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.Status != "withdrawn")
                .ToListAsync(cancellationToken);
            subs = subs.Where(x => ChatService.UserIdsMatchLoose(cid, x.CarrierUserId)).ToList();
            foreach (var s in subs)
            {
                s.Status = "withdrawn";
                s.UpdatedAtUtc = now;
            }

            PersistSheetPayloadWithAck(sheetRow, ack, now, p => ClearTransportistaPhonesForSubs(p, subs));
            await ApplyStoreTrustPenaltyOnSheetEditRejectAsync(thread.StoreId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await chat.PostAutomatedSystemThreadNoticeAsync(
                tid,
                SheetEditRejectNotice(carrierName, sheetTitle),
                cancellationToken);
        }
        else
        {
            PersistSheetPayloadWithAck(sheetRow, ack, now);
            await db.SaveChangesAsync(cancellationToken);
            await chat.PostAutomatedSystemThreadNoticeAsync(
                tid,
                SheetEditAcceptNotice(carrierName, sheetTitle),
                cancellationToken);
        }

        var emergentId = await EmergentPublicationIdForSheetAsync(tid, rsid, cancellationToken);
        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            tid,
            rsid,
            accept ? "sheet_edit_accept" : "sheet_edit_reject",
            cid,
            emergentId,
            cancellationToken);

        return true;
    }

    private static string? ResolveCarrierKeyInEditAck(
        IReadOnlyDictionary<string, string> byCarrier,
        string viewerId)
    {
        var v = (viewerId ?? "").Trim();
        if (v.Length < 2) return null;
        foreach (var kv in byCarrier)
        {
            var k = (kv.Key ?? "").Trim();
            if (k.Length == 0) continue;
            if (string.Equals(k, v, StringComparison.Ordinal))
                return kv.Key;
            if (ChatService.UserIdsMatchLoose(v, kv.Key))
                return kv.Key;
        }
        return null;
    }

    private static string TruncateRouteSheetTitle(string? titulo)
    {
        var t = (titulo ?? "").Trim();
        return t.Length <= 120 ? t : t[..120] + "…";
    }

    private static RouteSheetPayload CloneRoutePayload(RouteSheetPayload source) =>
        JsonSerializer.Deserialize<RouteSheetPayload>(
            JsonSerializer.Serialize(source, RouteSheetJson.Options),
            RouteSheetJson.Options) ?? source;

    private static void PersistSheetPayloadWithAck(
        ChatRouteSheetRow row,
        RouteSheetEditAckPayload ack,
        DateTimeOffset updatedAt,
        Action<RouteSheetPayload>? mutateCloned = null)
    {
        var p = CloneRoutePayload(row.Payload);
        mutateCloned?.Invoke(p);
        p.RouteSheetEditAck = ack;
        row.Payload = CloneRoutePayload(p);
        row.UpdatedAtUtc = updatedAt;
    }

    private static void ClearTransportistaPhonesForSubs(
        RouteSheetPayload payload,
        IReadOnlyList<RouteTramoSubscriptionRow> subs)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            var stopId = (sub.StopId ?? "").Trim();
            var parada =
                stopId.Length > 0
                    ? payload.Paradas.FirstOrDefault(p =>
                        string.Equals((p.Id ?? "").Trim(), stopId, StringComparison.Ordinal))
                    : null;
            if (parada is null && sub.StopOrden > 0)
                parada = payload.Paradas.FirstOrDefault(p => p.Orden == sub.StopOrden);
            if (parada is not null)
                parada.TelefonoTransportista = null;
        }
    }

    private async Task ApplyStoreTrustPenaltyOnSheetEditRejectAsync(
        string? storeId,
        CancellationToken cancellationToken)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return;
        var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (storeRow is null)
            return;
        storeRow.TrustScore = Math.Max(
            -10_000,
            storeRow.TrustScore - RouteSheetEditAckComputation.StoreTrustPenaltyOnCarrierRejectSheetEdit);
    }

    private async Task<string?> EmergentPublicationIdForSheetAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ThreadId == threadId && e.RouteSheetId == routeSheetId && e.RetractedAtUtc == null,
                cancellationToken);
        var id = (em?.Id ?? "").Trim();
        return id.Length > 0 ? id : null;
    }

    private static string SheetEditAcceptNotice(string carrierName, string sheetTitle) =>
        sheetTitle.Length > 0
            ? $"{carrierName} aceptó los cambios en la hoja de ruta «{sheetTitle}»."
            : $"{carrierName} aceptó los cambios en la hoja de ruta.";

    private static string SheetEditRejectNotice(string carrierName, string sheetTitle)
    {
        const string tail =
            " Sus tramos quedan libres en la oferta pública; salió del chat. A la tienda se aplicó un ajuste de confianza por la edición no aceptada.";
        return sheetTitle.Length > 0
            ? $"{carrierName} rechazó los cambios en «{sheetTitle}».{tail}"
            : $"{carrierName} rechazó los cambios en la hoja.{tail}";
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
