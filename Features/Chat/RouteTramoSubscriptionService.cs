using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Chat.Utils;
using VibeTrade.Backend.Features.Trust;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteTramoSubscriptionService(
    AppDbContext db,
    IChatService chat,
    ITrustScoreLedgerService trustLedger) : IRouteTramoSubscriptionService
{
    private const int CarrierRouteExitTrustPenalty = 3;

    private readonly record struct SellerTramoKey(
        string ActorId,
        string ThreadId,
        string RouteSheetId,
        string CarrierId,
        string StopRestrict);

    private static SellerTramoKey ToSellerKey(TramoSellerSheetAction a) =>
        new(
            (a.ActorUserId ?? "").Trim(),
            (a.ThreadId ?? "").Trim(),
            (a.RouteSheetId ?? "").Trim(),
            (a.CarrierUserId ?? "").Trim(),
            (a.StopId ?? "").Trim());

    public async Task RecordSubscriptionRequestAsync(
        RecordRouteTramoSubscriptionRequestArgs request,
        CancellationToken cancellationToken = default)
    {
        var (tid, rsid, sid, uid) = RouteTramoSubscriptionInputNormalize.TrimTramoRequestKeys(
            request.ThreadId, request.RouteSheetId, request.StopId, request.CarrierUserId);
        if (tid.Length < 2 || rsid.Length < 1 || sid.Length < 1 || uid.Length < 2)
            return;

        var (svcTrim, label, snap) = RouteTramoSubscriptionInputNormalize.NormalizeOptionalFields(
            request.StoreServiceId, request.TransportServiceLabel, request.CarrierContactPhone);
        var stopOrden = request.StopOrden;

        var existing = await db.RouteTramoSubscriptions
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.StopOrden = stopOrden;
            existing.StoreServiceId = svcTrim;
            existing.TransportServiceLabel = label.Length > 0 ? label : existing.TransportServiceLabel;
            existing.Status = "pending";
            existing.UpdatedAtUtc = now;
            if (snap is not null)
                existing.CarrierPhoneSnapshot = snap;
        }
        else
        {
            db.RouteTramoSubscriptions.Add(new RouteTramoSubscriptionRow
            {
                Id = "rts_" + Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                RouteSheetId = rsid,
                StopId = sid,
                StopOrden = stopOrden,
                CarrierUserId = uid,
                CarrierPhoneSnapshot = snap,
                StoreServiceId = svcTrim,
                TransportServiceLabel = label,
                Status = "pending",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListPublishedForThreadAsync(
        string viewerUserId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var uid = (viewerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null)
            return null;

        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken))
            return null;
        var narrowToCarrierOnly = !ChatThreadAccess.UserCanSeeThread(uid, thread);

        var publishedSheets = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null && x.PublishedToPlatform)
            .ToListAsync(cancellationToken);
        if (publishedSheets.Count == 0)
            return [];

        var publishedIds = publishedSheets.Select(x => x.RouteSheetId).ToHashSet(StringComparer.Ordinal);
        var payloads = publishedSheets.ToDictionary(x => x.RouteSheetId, x => x.Payload, StringComparer.Ordinal);

        var rows = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && publishedIds.Contains(x.RouteSheetId))
            .OrderBy(x => x.RouteSheetId)
            .ThenBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var list = await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
        if (narrowToCarrierOnly)
            list = RouteTramoSubscriptionDtoFilter.NarrowForCarrierViewer(uid, list);
        return list;
    }

    public async Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListForCarrierByEmergentPublicationAsync(
        string carrierUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var eid = (emergentOfferId ?? "").Trim();
        if (uid.Length < 2 || eid.Length < 4 || !RecommendationBatchOfferLoader.IsEmergentPublicationId(eid))
            return null;

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid && x.RetractedAtUtc == null, cancellationToken);
        if (em is null)
            return null;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == em.ThreadId
                    && x.RouteSheetId == em.RouteSheetId
                    && x.DeletedAtUtc == null
                    && x.PublishedToPlatform,
                cancellationToken);
        if (sheetRow is null)
            return [];

        var rows = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == em.ThreadId && x.RouteSheetId == em.RouteSheetId)
            .OrderBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var payloads = new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal)
        {
            [em.RouteSheetId] = sheetRow.Payload,
        };
        var list = await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
        return RouteTramoSubscriptionDtoFilter.NarrowForCarrierViewer(uid, list);
    }

    private async Task<List<RouteTramoSubscriptionItemDto>> ToSubscriptionItemDtosAsync(
        List<RouteTramoSubscriptionRow> rows,
        Dictionary<string, RouteSheetPayload> payloads,
        CancellationToken cancellationToken)
    {
        var carrierIds = rows.Select(x => x.CarrierUserId).Distinct(StringComparer.Ordinal).ToList();
        var accounts = await db.UserAccounts.AsNoTracking()
            .Where(x => carrierIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal, cancellationToken);

        var svcIds = rows
            .Select(x => x.StoreServiceId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var svcStores = svcIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.StoreServices.AsNoTracking()
                .Where(s => svcIds.Contains(s.Id) && s.DeletedAtUtc == null)
                .Select(s => new { s.Id, s.StoreId })
                .ToDictionaryAsync(x => x.Id, x => x.StoreId, StringComparer.Ordinal, cancellationToken);

        return rows
            .Select(r =>
                RouteTramoSubscriptionItemMapper.MapRow(
                    r,
                    payloads.GetValueOrDefault(r.RouteSheetId),
                    accounts,
                    svcStores))
            .ToList();
    }

    public async Task<int?> AcceptCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default)
    {
        var k = ToSellerKey(action);
        if (k.ActorId.Length < 2 || k.ThreadId.Length < 4 || k.RouteSheetId.Length < 1 || k.CarrierId.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ThreadId, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null
            || !string.Equals(thread.SellerUserId, k.ActorId, StringComparison.Ordinal))
            return null;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return null;

        var built = await BuildPendingToConfirmListAsync(k, cancellationToken);
        if (built is null)
            return null;
        var (toConfirm, subsInQuery) = built.Value;
        if (toConfirm.Count == 0)
            return subsInQuery > 0 ? 0 : null;

        var carrierAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.CarrierId, cancellationToken);
        var accountPhone = RouteTramoUserContactUtil.BestPhoneForCarrier(
            carrierAcc, null, null);

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toConfirm)
        {
            sub.Status = "confirmed";
            sub.UpdatedAtUtc = now;
            var parada = payload.Paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sub.StopId, StringComparison.Ordinal));
            var tel = accountPhone;
            if (tel.Length == 0)
                tel = (sub.CarrierPhoneSnapshot ?? "").Trim();
            if (tel.Length == 0 && parada is not null)
                tel = (parada.TelefonoTransportista ?? "").Trim();
            if (parada is not null && tel.Length > 0)
                parada.TelefonoTransportista = tel;
        }

        RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        await db.SaveChangesAsync(cancellationToken);

        var emergentPubId = await EmergentIdForThreadSheetAsync(
            k.ThreadId, k.RouteSheetId, cancellationToken);

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ActorId, cancellationToken);
        var deciderLabel = RouteTramoUserContactUtil.ParticipanteOrDisplay(actorAcc?.DisplayName);
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview = $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var carrierLabel = string.IsNullOrWhiteSpace(carrierAcc?.DisplayName)
            ? "El transportista"
            : carrierAcc!.DisplayName.Trim();
        var sellerInboxPreview =
            $"Confirmaste el servicio de transporte de {carrierLabel} en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        await chat.NotifyRouteTramoSubscriptionAcceptedAsync(
            new RouteTramoSubscriptionAcceptedNotificationArgs(
                k.CarrierId,
                k.ThreadId,
                preview,
                deciderLabel,
                deciderTrust,
                k.ActorId,
                k.ActorId,
                sellerInboxPreview,
                carrierLabel,
                carrierAcc?.TrustScore ?? 0),
            cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                k.ThreadId,
                k.RouteSheetId,
                "accept",
                k.ActorId,
                emergentPubId),
            cancellationToken);

        return toConfirm.Count;
    }

    private async Task<(List<RouteTramoSubscriptionRow> List, int SubsInQuery)?> BuildPendingToConfirmListAsync(
        SellerTramoKey k,
        CancellationToken cancellationToken)
    {
        IQueryable<RouteTramoSubscriptionRow> q = db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.CarrierUserId == k.CarrierId);
        if (k.StopRestrict.Length > 0)
            q = q.Where(x => x.StopId == k.StopRestrict);
        var subs = await q.ToListAsync(cancellationToken);
        if (k.StopRestrict.Length > 0 && subs.Count == 0)
            return null;

        var toConfirm = subs.Where(RouteTramoSubscriptionStatusUtil.IsPendingForSellerDecision).ToList();
        if (toConfirm.Count == 0)
            return (toConfirm, subs.Count);

        var stopsTakenByOthers = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == k.ThreadId
                && x.RouteSheetId == k.RouteSheetId
                && x.CarrierUserId != k.CarrierId
                && x.Status == "confirmed")
            .Select(x => x.StopId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var taken = stopsTakenByOthers.ToHashSet(StringComparer.Ordinal);
        toConfirm = toConfirm.Where(s => !taken.Contains(s.StopId)).ToList();
        if (toConfirm.Count == 0)
            throw new TramoSubscriptionAcceptConflictException(
                "Los tramos pendientes de este transportista ya tienen otro transportista confirmado.");

        return (toConfirm, subs.Count);
    }

    public async Task<int?> RejectCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default)
    {
        var k = ToSellerKey(action);
        if (k.ActorId.Length < 2 || k.ThreadId.Length < 4 || k.RouteSheetId.Length < 1 || k.CarrierId.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ThreadId, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null
            || !string.Equals(thread.SellerUserId, k.ActorId, StringComparison.Ordinal))
            return null;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return null;

        IQueryable<RouteTramoSubscriptionRow> q = db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.CarrierUserId == k.CarrierId);
        if (k.StopRestrict.Length > 0)
            q = q.Where(x => x.StopId == k.StopRestrict);
        var subs = await q.ToListAsync(cancellationToken);
        if (k.StopRestrict.Length > 0 && subs.Count == 0)
            return null;

        var toReject = subs.Where(RouteTramoSubscriptionStatusUtil.IsPendingForSellerDecision).ToList();
        if (toReject.Count == 0)
            return subs.Count > 0 ? 0 : null;

        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toReject)
        {
            sub.Status = "rejected";
            sub.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.RetractedAtUtc == null,
                cancellationToken);
        var routeOfferId = string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ActorId, cancellationToken);
        var (sellerLabel, sellerTrust) = RouteTramoSellerPresentation.LabelAndTrust(store, actorAcc);
        var preview =
            $"{sellerLabel} rechazó tu solicitud de transporte en un tramo de la hoja de ruta publicada. Podés revisar la oferta y los tramos disponibles.";

        await chat.NotifyRouteTramoSubscriptionRejectedAsync(
            new RouteTramoSubscriptionRejectedNotificationArgs(
                k.CarrierId,
                k.ThreadId,
                preview,
                sellerLabel,
                sellerTrust,
                k.ActorId,
                routeOfferId),
            cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(
                k.ThreadId,
                k.RouteSheetId,
                "reject",
                k.ActorId,
                routeOfferId),
            cancellationToken);

        return toReject.Count;
    }

    public async Task<CarrierWithdrawFromThreadResult?> WithdrawCarrierFromThreadAsync(
        string carrierUserId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken))
            return null;
        if (ChatThreadAccess.UserCanSeeThread(uid, thread))
            return null;

        var subs = await db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.CarrierUserId == uid && x.Status != "withdrawn")
            .ToListAsync(cancellationToken);
        if (subs.Count == 0)
            return null;

        var hadConfirmed = subs.Exists(x =>
            string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));

        var distinctSheetIds = subs.Select(x => x.RouteSheetId).Distinct().ToList();
        var anyRouteIncomplete = await AnyRouteNotDeliveredOnSheetsAsync(
            distinctSheetIds, tid, cancellationToken);

        var applyTrustPenalty = hadConfirmed && anyRouteIncomplete;
        if (thread.BuyerExpelledAtUtc is not null && thread.SellerExpelledAtUtc is not null)
            applyTrustPenalty = false;

        var now = DateTimeOffset.UtcNow;
        await ClearCarrierPhoneOnSheetsForWithdrawAsync(tid, subs, now, cancellationToken);
        MarkSubscriptionsWithdrawn(subs, now);
        int? trustScoreAfterPenalty = await ApplyTrustPenaltyIfNeededAsync(
            applyTrustPenalty, uid, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var display = await db.UserAccounts.AsNoTracking()
            .Where(x => x.Id == uid)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        var sys = RouteTramoWithdrawSystemText.BuildAutomatedNotice(
            display ?? "",
            subs.Count,
            distinctSheetIds.Count,
            applyTrustPenalty);
        await chat.PostAutomatedSystemThreadNoticeAsync(tid, sys, cancellationToken);

        foreach (var rsid in distinctSheetIds)
        {
            var emergentPubId = await EmergentIdForThreadSheetAsync(tid, rsid, cancellationToken);
            await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "withdraw", uid, emergentPubId),
                cancellationToken);
        }

        return new CarrierWithdrawFromThreadResult(subs.Count, applyTrustPenalty, trustScoreAfterPenalty);
    }

    private static void MarkSubscriptionsWithdrawn(List<RouteTramoSubscriptionRow> subs, DateTimeOffset now)
    {
        foreach (var s in subs)
        {
            s.Status = "withdrawn";
            s.UpdatedAtUtc = now;
        }
    }

    private async Task<int?> ApplyTrustPenaltyIfNeededAsync(
        bool apply,
        string uid,
        CancellationToken cancellationToken)
    {
        if (!apply)
            return null;
        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == uid, cancellationToken);
        if (acc is null)
            return null;
        var prev = acc.TrustScore;
        acc.TrustScore = Math.Max(-10_000, prev - CarrierRouteExitTrustPenalty);
        var after = acc.TrustScore;
        trustLedger.StageEntry(
            TrustLedgerSubjects.User,
            uid,
            acc.TrustScore - prev,
            acc.TrustScore,
            "Abandono de ruta como transportista antes de entregar (demo)");
        return after;
    }

    private async Task<bool> AnyRouteNotDeliveredOnSheetsAsync(
        List<string> distinctSheetIds,
        string tid,
        CancellationToken cancellationToken)
    {
        foreach (var rsid in distinctSheetIds)
        {
            var sh = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                    cancellationToken);
            if (sh is null)
                continue;
            if (RouteTramoRouteTrustUtil.IsRouteStateNotDelivered(sh.Payload))
                return true;
        }
        return false;
    }

    private async Task ClearCarrierPhoneOnSheetsForWithdrawAsync(
        string tid,
        List<RouteTramoSubscriptionRow> subs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var grp in subs.GroupBy(x => x.RouteSheetId))
        {
            var rsid = grp.Key;
            var sheetRow = await db.ChatRouteSheets
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                    cancellationToken);
            if (sheetRow is null)
                continue;
            var payload = sheetRow.Payload;
            payload.Paradas ??= new List<RouteStopPayload>();
            foreach (var sub in grp)
            {
                var parada = RouteTramoParadaResolver.FindByStopIdOrOrden(
                    payload.Paradas,
                    sub.StopId,
                    sub.StopOrden);
                if (parada is not null)
                    parada.TelefonoTransportista = null;
            }
            RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        }
    }

    private async Task<string?> EmergentIdForThreadSheetAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == threadId && x.RouteSheetId == routeSheetId && x.RetractedAtUtc == null,
                cancellationToken);
        return string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();
    }
}
