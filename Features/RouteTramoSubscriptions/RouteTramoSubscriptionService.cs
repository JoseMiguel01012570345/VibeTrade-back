using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Trust.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Trust;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

public sealed class RouteTramoSubscriptionService(
    AppDbContext db,
    IChatService chat,
    ITrustScoreLedgerService trustLedger,
    IRouteTramoSubscriptionNotificationService tramoNotifications) : IRouteTramoSubscriptionService
{
    /// <summary>Mensaje de <see cref="InvalidOperationException"/> cuando no se puede confirmar: otro transportista ya ocupa los tramos.</summary>
    public const string AcceptCarrierPendingConflictMessage =
        "Los tramos pendientes de este transportista ya tienen otro transportista confirmado.";

    private const int CarrierRouteExitTrustPenalty = 3;

    public async Task RecordSubscriptionRequestAsync(
        RecordRouteTramoSubscriptionRequestArgs request,
        CancellationToken cancellationToken = default)
    {
        var (tid, rsid, sid, uid) = SubscriptionsUtils.TrimTramoRequestKeys(
            request.ThreadId, request.RouteSheetId, request.StopId, request.CarrierUserId);
        if (tid.Length < 2 || rsid.Length < 1 || sid.Length < 1 || uid.Length < 2)
            return;

        var (svcTrim, label, snap) = SubscriptionsUtils.NormalizeOptionalFields(
            request.StoreServiceId, request.TransportServiceLabel, request.CarrierContactPhone);
        var stopOrden = request.StopOrden;

        var existing = await db.RouteTramoSubscriptions
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid,
                cancellationToken);

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        string? stopFp = null;
        if (sheetRow is not null)
        {
            var parada = sheetRow.Payload.Paradas?
                .FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (parada is not null)
                stopFp = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
        }

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
            if (stopFp is not null)
                existing.StopContentFingerprint = stopFp;
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
                StopContentFingerprint = stopFp,
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

        // Quien ya es comprador o vendedor del hilo debe ver el mismo alcance de suscripciones que las hojas del chat
        // (GET route-sheets lista todas las no eliminadas). Si sólo usáramos UserCanSeeThread, antes del primer mensaje
        // el comprador podía quedar como «narrow» y el GET coincidía sólo con hojas publicadas — distinto al transportista.
        var buyerId = (thread.BuyerUserId ?? "").Trim();
        var sellerId = (thread.SellerUserId ?? "").Trim();
        var isBuyerOrSellerParty =
            (buyerId.Length > 0 && string.Equals(uid, buyerId, StringComparison.Ordinal))
            || (sellerId.Length > 0 && string.Equals(uid, sellerId, StringComparison.Ordinal));
        var narrowToCarrierOnly = !isBuyerOrSellerParty && !ChatThreadAccess.UserCanSeeThread(uid, thread);

        Dictionary<string, ChatRouteSheetRow> sheetsById;
        if (narrowToCarrierOnly)
        {
            var publishedSheets = await db.ChatRouteSheets.AsNoTracking()
                .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null && x.PublishedToPlatform)
                .ToListAsync(cancellationToken);
            sheetsById = publishedSheets
                .ToDictionary(x => x.RouteSheetId, x => x, StringComparer.Ordinal);

            // Transportista sin Initiator/FirstMessage: puede tener suscripción (p. ej. presel) en hoja aún no publicada;
            // sin esto GET devuelve [] y el cliente bloquea el chat aunque UserCanAccessThreadRowAsync sea true.
            var carrierRows = await db.RouteTramoSubscriptions.AsNoTracking()
                .Where(x => x.ThreadId == tid && x.Status != "withdrawn")
                .Select(x => new { x.RouteSheetId, x.CarrierUserId })
                .ToListAsync(cancellationToken);
            var mySubSheetIds = carrierRows
                .Where(x => ChatThreadAccess.UserIdsMatchLoose(uid, x.CarrierUserId))
                .Select(x => x.RouteSheetId)
                .Distinct()
                .ToList();
            var missingIds = mySubSheetIds.Where(id => !sheetsById.ContainsKey(id)).ToList();
            if (missingIds.Count > 0)
            {
                var extraSheets = await db.ChatRouteSheets.AsNoTracking()
                    .Where(x =>
                        x.ThreadId == tid
                        && x.DeletedAtUtc == null
                        && missingIds.Contains(x.RouteSheetId))
                    .ToListAsync(cancellationToken);
                foreach (var row in extraSheets)
                    sheetsById[row.RouteSheetId] = row;
            }
        }
        else
        {
            var threadSheets = await db.ChatRouteSheets.AsNoTracking()
                .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null)
                .ToListAsync(cancellationToken);
            sheetsById = threadSheets
                .ToDictionary(x => x.RouteSheetId, x => x, StringComparer.Ordinal);
        }

        if (sheetsById.Count == 0)
            return [];

        var sheetIds = sheetsById.Keys.ToHashSet(StringComparer.Ordinal);
        var payloads = sheetsById.ToDictionary(x => x.Key, x => x.Value.Payload, StringComparer.Ordinal);

        var rows = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && sheetIds.Contains(x.RouteSheetId))
            .OrderBy(x => x.RouteSheetId)
            .ThenBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var list = await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
        if (narrowToCarrierOnly)
            list = SubscriptionsUtils.NarrowItemsForCarrierViewer(uid, list);
        return list;
    }

    public async Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListForCarrierByEmergentPublicationAsync(
        string carrierUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var eid = (emergentOfferId ?? "").Trim();
        if (uid.Length < 2 || eid.Length < 4 || !OfferUtils.IsEmergentPublicationId(eid))
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
        return SubscriptionsUtils.NarrowItemsForCarrierViewer(uid, list);
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
                SubscriptionsUtils.MapSubscriptionItem(
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
        var k = SellerTramoKey.FromAction(action);
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
        var accountPhone = SubscriptionsUtils.BestPhoneForCarrier(
            carrierAcc, null, null);

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        var now = DateTimeOffset.UtcNow;
        var metaStops = new List<(string StopId, string? StoreServiceId)>();
        foreach (var sub in toConfirm)
        {
            sub.Status = "confirmed";
            sub.UpdatedAtUtc = now;
            var parada = payload.Paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sub.StopId, StringComparison.Ordinal));
            if (parada is not null)
                sub.StopContentFingerprint = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
            var tel = accountPhone;
            if (tel.Length == 0)
                tel = (sub.CarrierPhoneSnapshot ?? "").Trim();
            if (tel.Length == 0 && parada is not null)
                tel = (parada.TelefonoTransportista ?? "").Trim();
            if (parada is not null && tel.Length > 0)
                parada.TelefonoTransportista = tel;
            if (parada is not null && !string.IsNullOrWhiteSpace(sub.StoreServiceId))
            {
                parada.TransportInvitedStoreServiceId = sub.StoreServiceId.Trim();
                if (!string.IsNullOrWhiteSpace(sub.TransportServiceLabel))
                    parada.TransportInvitedServiceSummary = sub.TransportServiceLabel.Trim();
            }

            metaStops.Add((sub.StopId, sub.StoreServiceId));
        }

        RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        await db.SaveChangesAsync(cancellationToken);

        await ApplyConfirmedCarriersAsync(
                k.ThreadId,
                k.RouteSheetId,
                metaStops.Select(x => x.StopId).ToList(),
                cancellationToken)
            .ConfigureAwait(false);

        await tramoNotifications.NotifyLegHandoffsAfterCarrierConfirmedAsync(
            k.ThreadId,
            k.RouteSheetId,
            payload,
            metaStops.Select(x => x.StopId).ToList(),
            cancellationToken)
            .ConfigureAwait(false);

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ActorId, cancellationToken);
        var deciderLabel = SubscriptionsUtils.ParticipanteOrDisplay(actorAcc?.DisplayName);
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview = $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var carrierLabel = string.IsNullOrWhiteSpace(carrierAcc?.DisplayName)
            ? "El transportista"
            : carrierAcc!.DisplayName.Trim();
        var sellerInboxPreview =
            $"Confirmaste el servicio de transporte de {carrierLabel} en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var acceptedMeta =
            RouteTramoSubscriptionNotificationService.BuildAcceptMetaJson(k.RouteSheetId, k.CarrierId, metaStops);

        await tramoNotifications.NotifyTramoSubscriptionAcceptedAndBroadcastAsync(
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
                carrierAcc?.TrustScore ?? 0,
                acceptedMeta),
            k.ThreadId,
            k.RouteSheetId,
            "accept",
            k.ActorId,
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

        var toConfirm = subs.Where(SubscriptionsUtils.IsPendingForSellerDecision).ToList();
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
            throw new InvalidOperationException(AcceptCarrierPendingConflictMessage);

        return (toConfirm, subs.Count);
    }

    public async Task<int?> RejectCarrierPendingOnSheetAsync(
        TramoSellerSheetAction action,
        CancellationToken cancellationToken = default)
    {
        var k = SellerTramoKey.FromAction(action);
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

        var toReject = subs.Where(SubscriptionsUtils.IsPendingForSellerDecision).ToList();
        if (toReject.Count == 0)
            return subs.Count > 0 ? 0 : null;

        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toReject)
        {
            sub.Status = "rejected";
            sub.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == k.ActorId, cancellationToken);
        var (sellerLabel, sellerTrust) = SubscriptionsUtils.SellerLabelAndTrust(store, actorAcc);
        var preview =
            $"{sellerLabel} rechazó tu solicitud de transporte en un tramo de la hoja de ruta publicada. Puedes revisar la oferta y los tramos disponibles.";

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == k.ThreadId && x.RouteSheetId == k.RouteSheetId && x.RetractedAtUtc == null,
                cancellationToken);
        var routeOfferId = string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();

        await tramoNotifications.NotifyTramoSubscriptionRejectedAndBroadcastAsync(
            new RouteTramoSubscriptionRejectedNotificationArgs(
                k.CarrierId,
                k.ThreadId,
                preview,
                sellerLabel,
                sellerTrust,
                k.ActorId,
                routeOfferId),
            k.ThreadId,
            k.RouteSheetId,
            k.ActorId,
            cancellationToken);

        return toReject.Count;
    }

    public async Task<CarrierExpelledBySellerResult?> ExpelCarrierBySellerFromThreadAsync(
        string sellerUserId,
        string threadId,
        string carrierUserId,
        string reason,
        string? routeSheetId = null,
        string? stopId = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = await TryPrepareSellerExpelContextAsync(
            sellerUserId,
            threadId,
            carrierUserId,
            reason,
            routeSheetId,
            stopId,
            cancellationToken);
        if (ctx is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        await ApplyRouteDeliveryRefundEligibilityForCarrierRemovalAsync(
                ctx.ThreadId,
                ctx.CarrierUserId,
                ctx.Subs,
                RouteStopRefundEligibleReasons.CarrierExpelled,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        await ClearCarrierPhoneOnSheetsForWithdrawAsync(ctx.ThreadId, ctx.Subs, now, cancellationToken);
        SubscriptionsUtils.MarkSubscriptionsWithdrawn(ctx.Subs, now);
        int? storeTrustAfter = await ApplyStoreTrustPenaltyForSellerExpelIfNeededAsync(
            ctx.ApplyStoreTrustPenalty,
            ctx.ConfirmedStopsWithdrawnCount,
            ctx.Thread.StoreId,
            ctx.ReasonTrim,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (ctx.ApplyStoreTrustPenalty && storeTrustAfter is int balAfter)
            await tramoNotifications.NotifySellerTrustPenaltyAfterConfirmedExpelAsync(ctx, balAfter, cancellationToken);

        await tramoNotifications.PublishSellerExpelledNotificationsAsync(ctx, cancellationToken);

        return new CarrierExpelledBySellerResult(
            ctx.Subs.Count,
            ctx.ApplyStoreTrustPenalty,
            storeTrustAfter,
            ctx.ConfirmedStopsWithdrawnCount,
            ctx.CarrierFullyRemovedFromThread);
    }

    private async Task<SellerExpelContext?> TryPrepareSellerExpelContextAsync(
        string sellerUserId,
        string threadId,
        string carrierUserId,
        string reason,
        string? routeSheetId,
        string? stopId,
        CancellationToken cancellationToken)
    {
        var sid = (sellerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var carrierId = (carrierUserId ?? "").Trim();
        var reasonTrim = (reason ?? "").Trim();
        if (sid.Length < 2 || tid.Length < 4 || carrierId.Length < 2 || reasonTrim.Length < 1)
            return null;
        if (reasonTrim.Length > 2000)
            reasonTrim = reasonTrim[..2000];

        var filterRs = (routeSheetId ?? "").Trim();
        var filterStop = (stopId ?? "").Trim();
        var expelSingleTramo = filterRs.Length > 0 && filterStop.Length > 0;

        var thread = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null
            || !string.Equals(thread.SellerUserId, sid, StringComparison.Ordinal))
            return null;

        if (string.Equals(carrierId, thread.BuyerUserId, StringComparison.Ordinal)
            || string.Equals(carrierId, thread.SellerUserId, StringComparison.Ordinal))
            return null;

        var subs = await db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.CarrierUserId == carrierId && x.Status != "withdrawn")
            .ToListAsync(cancellationToken);

        var hasDeliveryOnStop = await db.RouteStopDeliveries.AsNoTracking()
        .AnyAsync(
            x => x.ThreadId == tid &&
            x.RouteSheetId == filterRs &&
            x.RouteStopId == filterStop &&
            x.CurrentOwnerUserId == carrierId &&
            x.State != RouteStopDeliveryStates.EvidenceAccepted,
            cancellationToken);

        if (subs.Count == 0)
            return null;

        if (expelSingleTramo)
            subs = subs.Where(x => x.RouteSheetId == filterRs && x.StopId == filterStop).ToList();
        if (subs.Count == 0)
            return null;

        var hadConfirmed = subs.Exists(x =>
            string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));

        var confirmedStopsWithdrawnCount = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.RouteSheetId, x.StopId))
            .Distinct()
            .Count();

        var withdrawingIds = subs.Select(x => x.Id).ToHashSet();
        var hadOtherActive = await db.RouteTramoSubscriptions
            .AnyAsync(
                x => x.ThreadId == tid
                    && x.CarrierUserId == carrierId
                    && x.Status != "withdrawn"
                    && !withdrawingIds.Contains(x.Id),
                cancellationToken);

        var applyStoreTrustPenalty = hadConfirmed
            && thread.BuyerExpelledAtUtc is null
            && thread.SellerExpelledAtUtc is null
            && !hasDeliveryOnStop;

        var distinctSheetIds = subs.Select(x => x.RouteSheetId).Distinct().ToList();

        return new SellerExpelContext(
            thread,
            sid,
            tid,
            carrierId,
            reasonTrim,
            expelSingleTramo,
            subs,
            confirmedStopsWithdrawnCount,
            !hadOtherActive,
            applyStoreTrustPenalty,
            distinctSheetIds);
    }

    private static bool DeliveryStateAllowsCarrierWithdraw(string? stateRaw)
    {
        var s = (stateRaw ?? "").Trim();
        if (string.Equals(s, RouteStopDeliveryStates.IdleStoreCustody, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(s, RouteStopDeliveryStates.EvidenceAccepted, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(s, RouteStopDeliveryStates.AwaitingCarrierForHandoff, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> TryGetCarrierWithdrawConfirmedStopsDeliveryGateErrorAsync(
        string threadId,
        List<RouteTramoSubscriptionRow> subs,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var keys = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => ((x.RouteSheetId ?? "").Trim(), (x.StopId ?? "").Trim()))
            .Where(t => t.Item1.Length > 0 && t.Item2.Length > 0)
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return null;

        foreach (var (rsid, stopId) in keys)
        {
            var rows = await db.RouteStopDeliveries.AsNoTracking()
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == stopId)
                .Select(x => x.State)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (rows.Count == 0)
                continue;

            if (!rows.TrueForAll(DeliveryStateAllowsCarrierWithdraw))
                return "carrier_route_active";
        }

        return null;
    }

    public async Task<CarrierWithdrawFromThreadResult?> WithdrawCarrierFromThreadAsync(
        string carrierUserId,
        string threadId,
        string withdrawReason,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var reasonTrim = (withdrawReason ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4)
            return null;
        if (reasonTrim.Length < 1)
            return new CarrierWithdrawFromThreadResult(0, false, null) { ErrorCode = "carrier_withdraw_reason_required" };
        if (reasonTrim.Length > 2000)
            reasonTrim = reasonTrim[..2000];

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

        var confirmedStopCount = subs.Count(x =>
            string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));

        var hasRejectedRouteEvidence = await db.CarrierDeliveryEvidences.AsNoTracking()
            .AnyAsync(
                e =>
                    e.ThreadId == tid
                    && e.CarrierUserId == uid
                    && e.Status == ServiceEvidenceStatuses.Rejected
                    && db.RouteTramoSubscriptions.Any(s =>
                        s.ThreadId == tid
                        && s.RouteSheetId == e.RouteSheetId
                        && s.StopId == e.RouteStopId
                        && s.CarrierUserId == uid
                        && s.Status == "confirmed"),
                cancellationToken)
            .ConfigureAwait(false);
        if (hasRejectedRouteEvidence)
            return new CarrierWithdrawFromThreadResult(0, false, null) { ErrorCode = "carrier_route_evidence_rejected" };

        if (await HasPostCedeNonterminalRouteDeliveriesAsync(tid, uid, subs, cancellationToken).ConfigureAwait(false))
            return new CarrierWithdrawFromThreadResult(0, false, null) { ErrorCode = "carrier_route_post_cede_pending" };

        var holdsOperationalOwnership = await db.RouteStopDeliveries.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.CurrentOwnerUserId == uid
                    && x.State != RouteStopDeliveryStates.EvidenceAccepted
                    && x.State != RouteStopDeliveryStates.Refunded,
                cancellationToken)
            .ConfigureAwait(false);
        if (holdsOperationalOwnership)
            return new CarrierWithdrawFromThreadResult(0, false, null) { ErrorCode = "carrier_holds_ownership" };

        var gateErr = await TryGetCarrierWithdrawConfirmedStopsDeliveryGateErrorAsync(tid, subs, cancellationToken)
            .ConfigureAwait(false);
        if (gateErr is not null)
            return new CarrierWithdrawFromThreadResult(0, false, null) { ErrorCode = gateErr };

        var hadConfirmed = confirmedStopCount > 0;

        var distinctSheetIds = subs.Select(x => x.RouteSheetId).Distinct().ToList();

        var expelledParty =
            thread.BuyerExpelledAtUtc is not null || thread.SellerExpelledAtUtc is not null;
        var carrierLeaveWithoutTrustPenalty = false;
        if (hadConfirmed)
        {
            carrierLeaveWithoutTrustPenalty =
                await AllConfirmedRouteSheetsMarkedDeliveredAsync(tid, subs, cancellationToken)
                || await AllCarrierConfirmedStopsLogisticallyResolvedAsync(tid, uid, subs, cancellationToken);
        }

        // Penalización si tenía tramos confirmados y aún había obligaciones abiertas (hoja no entregada y tramos sin cierre logístico).
        var applyTrustPenalty = hadConfirmed && !expelledParty && !carrierLeaveWithoutTrustPenalty;

        var now = DateTimeOffset.UtcNow;
        await ApplyRouteDeliveryRefundEligibilityForCarrierRemovalAsync(
                tid,
                uid,
                subs,
                RouteStopRefundEligibleReasons.CarrierExit,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        await ClearCarrierPhoneOnSheetsForWithdrawAsync(tid, subs, now, cancellationToken);
        SubscriptionsUtils.MarkSubscriptionsWithdrawn(subs, now);
        int? trustScoreAfterPenalty = await ApplyTrustPenaltyIfNeededAsync(
            applyTrustPenalty, uid, confirmedStopCount, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var display = await db.UserAccounts.AsNoTracking()
            .Where(x => x.Id == uid)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        var sys = SubscriptionsUtils.BuildWithdrawAutomatedNotice(
            display ?? "",
            subs.Count,
            distinctSheetIds.Count,
            applyTrustPenalty,
            noPenaltyBecauseSheetsDelivered: hadConfirmed && !applyTrustPenalty && !expelledParty && carrierLeaveWithoutTrustPenalty,
            reasonTrim);
        await tramoNotifications.PostCarrierWithdrawSystemNoticeAndBroadcastsAsync(
            tid,
            sys,
            uid,
            distinctSheetIds,
            cancellationToken);

        return new CarrierWithdrawFromThreadResult(subs.Count, applyTrustPenalty, trustScoreAfterPenalty);
    }

    private async Task ApplyRouteDeliveryRefundEligibilityForCarrierRemovalAsync(
        string threadId,
        string carrierUserId,
        List<RouteTramoSubscriptionRow> subs,
        string refundReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        if (tid.Length < 4 || cid.Length < 2 || subs.Count == 0)
            return;

        var keys = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => ((x.RouteSheetId ?? "").Trim(), (x.StopId ?? "").Trim()))
            .Where(t => t.Item1.Length > 0 && t.Item2.Length > 0)
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return;

        var deliveries = await db.RouteStopDeliveries
                .Where(x => x.ThreadId == tid && x.CurrentOwnerUserId == cid && x.RefundedAtUtc == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            ;

        foreach (var d in deliveries)
        {
            var match = keys.Any(k =>
                string.Equals(k.Item1, d.RouteSheetId.Trim(), StringComparison.Ordinal)
                && string.Equals(k.Item2, d.RouteStopId.Trim(), StringComparison.Ordinal));
            if (!match)
                continue;

            if (d.State == RouteStopDeliveryStates.EvidenceAccepted)
                continue;

            d.RefundEligibleReason = refundReason;
            d.RefundEligibleSinceUtc = now;
            d.CurrentOwnerUserId = null;
            d.State = RouteStopDeliveryStates.AwaitingCarrierForHandoff;
            d.UpdatedAtUtc = now;

            db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
            {
                Id = "coe_" + Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                RouteSheetId = d.RouteSheetId,
                RouteStopId = d.RouteStopId,
                CarrierUserId = cid,
                Action = CarrierOwnershipActions.Released,
                AtUtc = now,
                Reason = refundReason,
            });
        }
    }

    public async Task<bool> CarrierRespondPreselectedRouteInviteAsync(
        CarrierPreselInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        var core = await PreselLoadCoreAsync(request, cancellationToken);
        if (core is null)
            return false;
        var digits = (core.Carrier.PhoneDigits ?? "").Trim();
        var stops = SubscriptionsUtils.PreselMatchStops(core.Payload, digits, request.StopIdRestrict);

        if (stops.Count == 0)
            return false;
        return request.Accepted
            ? await PreselExecuteAcceptAsync(core, stops, cancellationToken)
            : await PreselExecuteRejectAsync(core, stops, cancellationToken);
    }

    private async Task<PreselCore?> PreselLoadCoreAsync(
        CarrierPreselInviteRequest request,
        CancellationToken cancellationToken)
    {
        var uid = (request.CarrierUserId ?? "").Trim();
        var tid = (request.ThreadId ?? "").Trim();
        var rsid = (request.RouteSheetId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || rsid.Length < 1)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;

        var carrier = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == uid, cancellationToken);
        if (carrier is null)
            return null;
        var carrierDigits = (carrier.PhoneDigits ?? "").Trim();
        if (carrierDigits.Length < 6)
            return null;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null)
            return null;

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        return new PreselCore(thread, carrier, payload, rsid);
    }

    private async Task<(string? StoreServiceId, string TransportServiceLabel)> ResolvePreselInvitedStoreServiceAsync(
        string carrierUserId,
        RouteStopPayload? stop,
        CancellationToken cancellationToken)
    {
        var reqId = (stop?.TransportInvitedStoreServiceId ?? "").Trim();
        if (reqId.Length < 2)
            return (null, SubscriptionsUtils.PreselDefaultTransportServiceLabel);

        var row = await db.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .FirstOrDefaultAsync(s => s.Id == reqId, cancellationToken);
        // Mismo criterio que el catálogo: null = publicado; solo false oculta.
        if (row is null || row.DeletedAtUtc is not null)
            return (null, SubscriptionsUtils.PreselDefaultTransportServiceLabel);
        var owner = (row.Store?.OwnerUserId ?? "").Trim();
        if (!ChatThreadAccess.UserIdsMatchLoose(carrierUserId, owner))
            return (null, SubscriptionsUtils.PreselDefaultTransportServiceLabel);
        
        var label = (stop?.TransportInvitedServiceSummary ?? "").Trim();
        if (label.Length == 0)
        {
            var tipo = (row.TipoServicio ?? "").Trim();
            var cat = (row.Category ?? "").Trim();
            if (tipo.Length > 0 && cat.Length > 0)
                label = $"{tipo} · {cat}";
            else if (tipo.Length > 0)
                label = tipo;
            else if (cat.Length > 0)
                label = cat;
        }
        if (label.Length == 0)
            label = "Servicio de catálogo";
        return (reqId, label);
    }

    private async Task<bool> PreselExecuteAcceptAsync(
        PreselCore core,
        List<RouteStopPayload> stops,
        CancellationToken cancellationToken)
    {
        var tid = core.Thread.Id;
        var rsid = core.Rsid;
        var uid = (core.Carrier.Id ?? "").Trim();
        var sellerId = (core.Thread.SellerUserId ?? "").Trim();
        if (uid.Length < 2 || sellerId.Length < 2)
            return false;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null)
            return false;

        var phoneSnapRaw = SubscriptionsUtils.PhoneSnapForCarrier(core.Carrier);
        var phoneSnap = phoneSnapRaw.Length > 0 ? phoneSnapRaw : null;
        var accountPhone = SubscriptionsUtils.BestPhoneForCarrier(core.Carrier, null, null);

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        var now = DateTimeOffset.UtcNow;
        var preselMetaStops = new List<(string StopId, string? StoreServiceId)>();

        foreach (var stop in stops)
        {
            var sid = (stop.Id ?? "").Trim();
            if (sid.Length < 1)
                continue;

            string? stopFp = null;
            if (stop is not null)
                stopFp = RouteSheetEditAckComputation.RouteStopFingerprint(stop);

            var (invSvc, invLabel) = await ResolvePreselInvitedStoreServiceAsync(uid, stop, cancellationToken);
            preselMetaStops.Add((sid, invSvc));

            var existing = await db.RouteTramoSubscriptions
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid
                        && x.RouteSheetId == rsid
                        && x.StopId == sid
                        && x.CarrierUserId == uid,
                    cancellationToken);

            RouteTramoSubscriptionRow subRow;
            if (existing is null)
            {
                subRow = new RouteTramoSubscriptionRow
                {
                    Id = "rts_" + Guid.NewGuid().ToString("N"),
                    ThreadId = tid,
                    RouteSheetId = rsid,
                    StopId = sid,
                    StopOrden = stop.Orden,
                    CarrierUserId = uid,
                    CarrierPhoneSnapshot = phoneSnap,
                    StoreServiceId = invSvc,
                    TransportServiceLabel = invLabel,
                    Status = "confirmed",
                    StopContentFingerprint = stopFp,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.RouteTramoSubscriptions.Add(subRow);
            }
            else
            {
                subRow = existing;
                subRow.StopOrden = stop.Orden;
                subRow.StoreServiceId = invSvc;
                subRow.TransportServiceLabel = invLabel;
                subRow.Status = "confirmed";
                subRow.UpdatedAtUtc = now;
                if (phoneSnap is not null)
                    subRow.CarrierPhoneSnapshot = phoneSnap;
                if (stopFp is not null)
                    subRow.StopContentFingerprint = stopFp;
            }

            var tel = accountPhone;
            if (tel.Length == 0)
                tel = (subRow.CarrierPhoneSnapshot ?? "").Trim();
            if (tel.Length == 0)
                tel = (stop?.TelefonoTransportista ?? "").Trim();
            if (stop is not null && tel.Length > 0)
                stop.TelefonoTransportista = tel;
            if (stop is not null)
                SubscriptionsUtils.ApplyPreselAcceptedFieldsToParada(stop, invSvc, invLabel);
        }

        RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        await db.SaveChangesAsync(cancellationToken);

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sellerId, cancellationToken);
        var deciderLabel = SubscriptionsUtils.ParticipanteOrDisplay(actorAcc?.DisplayName);
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview =
            $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var carrierLabel = string.IsNullOrWhiteSpace(core.Carrier.DisplayName)
            ? "El transportista"
            : core.Carrier.DisplayName.Trim();
        var sellerInboxPreview =
            $"Confirmaste el servicio de transporte de {carrierLabel} en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var preselAcceptedMeta =
            RouteTramoSubscriptionNotificationService.BuildAcceptMetaJson(rsid, uid, preselMetaStops);

        await tramoNotifications.NotifyPreselAcceptAndBroadcastAsync(
            new RouteTramoSubscriptionAcceptedNotificationArgs(
                uid,
                tid,
                preview,
                deciderLabel,
                deciderTrust,
                sellerId,
                sellerId,
                sellerInboxPreview,
                carrierLabel,
                core.Carrier.TrustScore,
                preselAcceptedMeta),
            tid,
            rsid,
            sellerId,
            cancellationToken);
        return true;
    }

    private async Task<bool> PreselExecuteRejectAsync(
        PreselCore core,
        IReadOnlyList<RouteStopPayload> stops,
        CancellationToken cancellationToken)
    {
        var sellerId = (core.Thread.SellerUserId ?? "").Trim();
        if (sellerId.Length < 2)
            return false;

        var tid = core.Thread.Id;
        var rsid = core.Rsid;
        var uid = (core.Carrier.Id ?? "").Trim();
        if (uid.Length < 2)
            return false;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (sheetRow is not null)
        {
            var payload = sheetRow.Payload;
            payload.Paradas ??= new List<RouteStopPayload>();
            var stopIds = stops
                .Select(s => (s.Id ?? "").Trim())
                .Where(id => id.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var stop in stops)
            {
                var sid = (stop.Id ?? "").Trim();
                if (sid.Length < 1)
                    continue;
                var parada = SubscriptionsUtils.FindParadaByStopIdOrOrden(
                    payload.Paradas,
                    sid,
                    stop.Orden);
                if (parada is not null)
                {
                    parada.TelefonoTransportista = null;
                    parada.TransportInvitedStoreServiceId = null;
                    parada.TransportInvitedServiceSummary = null;
                }
            }

            if (stopIds.Count > 0)
            {
                var subsToReject = await db.RouteTramoSubscriptions
                    .Where(x =>
                        x.ThreadId == tid
                        && x.RouteSheetId == rsid
                        && x.CarrierUserId == uid
                        && stopIds.Contains(x.StopId))
                    .ToListAsync(cancellationToken);
                foreach (var sub in subsToReject)
                {
                    sub.Status = "rejected";
                    sub.StoreServiceId = null;
                    sub.UpdatedAtUtc = now;
                }
            }

            RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        var carrierName = (core.Carrier.DisplayName ?? "").Trim();
        if (carrierName.Length == 0)
            carrierName = "Transportista";
        var title = (core.Payload.Titulo ?? "").Trim();
        var preview = title.Length > 0
            ? $"{carrierName} rechazó la invitación como transportista en «{title}»."
            : $"{carrierName} rechazó la invitación como transportista en una hoja de ruta.";

        var oid = (core.Thread.OfferId ?? "").Trim();
        if (oid.Length < 2)
            return false;

        await tramoNotifications.PublishPreselCarrierDeclinedAsync(
            sheetRow is not null,
            tid,
            rsid,
            uid,
            new RouteSheetPreselDeclinedByCarrierNotificationArgs(
                sellerId,
                tid,
                oid,
                core.Rsid,
                carrierName,
                core.Carrier.TrustScore,
                uid,
                preview),
            cancellationToken);
        return true;
    }

    /// <summary>
    /// Cada hoja que tiene al menos un tramo confirmado en <paramref name="subs"/> debe existir y tener <see cref="RouteSheetPayload.Estado"/> «entregada».
    /// </summary>
    private async Task<bool> AllConfirmedRouteSheetsMarkedDeliveredAsync(
        string threadId,
        List<RouteTramoSubscriptionRow> subs,
        CancellationToken cancellationToken)
    {
        var sheetIds = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.RouteSheetId ?? "").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (sheetIds.Count == 0)
            return true;

        var rows = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == threadId && sheetIds.Contains(x.RouteSheetId) && x.DeletedAtUtc == null)
            .Select(x => new { x.RouteSheetId, x.Payload })
            .ToListAsync(cancellationToken);

        if (rows.Count != sheetIds.Count)
            return false;

        var bySheet = rows.ToDictionary(r => r.RouteSheetId, StringComparer.Ordinal);
        foreach (var sid in sheetIds)
        {
            if (!bySheet.TryGetValue(sid, out var row))
                return false;
            var estado = (row.Payload.Estado ?? "").Trim();
            if (!string.Equals(estado, "entregada", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Tras ceder titularidad en un tramo, el transportista no puede abandonar el hilo mientras la entrega de ese tramo
    /// no esté cerrada (evidencia aceptada o reembolso).
    /// </summary>
    private async Task<bool> HasPostCedeNonterminalRouteDeliveriesAsync(
        string threadId,
        string carrierUserId,
        List<RouteTramoSubscriptionRow> subs,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var uid = (carrierUserId ?? "").Trim();
        if (tid.Length < 4 || uid.Length < 2)
            return false;

        var keys = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => ((x.RouteSheetId ?? "").Trim(), (x.StopId ?? "").Trim()))
            .Where(t => t.Item1.Length > 0 && t.Item2.Length > 0)
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return false;

        foreach (var (rsid, stopId) in keys)
        {
            var cededHere = await db.CarrierOwnershipEvents.AsNoTracking()
                .AnyAsync(
                    e =>
                        e.ThreadId == tid
                        && e.RouteSheetId == rsid
                        && e.RouteStopId == stopId
                        && e.CarrierUserId == uid
                        && e.Action == CarrierOwnershipActions.Released
                        && (e.Reason == "carrier_cede" || e.Reason == "end_of_route"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!cededHere)
                continue;

            var stillOpen = await db.RouteStopDeliveries.AsNoTracking()
                .AnyAsync(
                    d =>
                        d.ThreadId == tid
                        && d.RouteSheetId == rsid
                        && d.RouteStopId == stopId
                        && d.RefundedAtUtc == null
                        && d.State != RouteStopDeliveryStates.Unpaid
                        && d.State != RouteStopDeliveryStates.EvidenceAccepted
                        && d.State != RouteStopDeliveryStates.Refunded,
                    cancellationToken)
                .ConfigureAwait(false);
            if (stillOpen)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cada tramo <b>confirmado</b> del transportista tiene filas de entrega y todas están en estado terminal
    /// (evidencia aceptada o reembolso por vencimiento/salida): ya no queda logística activa en esos tramos.
    /// </summary>
    private async Task<bool> AllCarrierConfirmedStopsLogisticallyResolvedAsync(
        string threadId,
        string carrierUserId,
        List<RouteTramoSubscriptionRow> subs,
        CancellationToken cancellationToken)
    {
        var cid = (carrierUserId ?? "").Trim();
        if (cid.Length < 2)
            return false;

        var keys = subs
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(x => ((x.RouteSheetId ?? "").Trim(), (x.StopId ?? "").Trim()))
            .Where(t => t.Item1.Length > 0 && t.Item2.Length > 0)
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return true;

        foreach (var (rsid, stopId) in keys)
        {
            var states = await db.RouteStopDeliveries.AsNoTracking()
                .Where(x =>
                    x.ThreadId == threadId
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == stopId)
                .Select(x => x.State)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (states.Count == 0)
                return false;

            var allTerminal = states.TrueForAll(SubscriptionsUtils.IsCarrierLegTrustTerminalState);
            if (!allTerminal)
                return false;
        }

        return true;
    }

    private async Task<int?> ApplyTrustPenaltyIfNeededAsync(
        bool apply,
        string uid,
        int confirmedStopCount,
        CancellationToken cancellationToken)
    {
        if (!apply || confirmedStopCount <= 0)
            return null;
        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == uid, cancellationToken);
        if (acc is null)
            return null;
        var prev = acc.TrustScore;
        var deltaTotal = -CarrierRouteExitTrustPenalty * confirmedStopCount;
        acc.TrustScore = Math.Max(-10_000, prev + deltaTotal);
        var after = acc.TrustScore;
        var tramosTxt = confirmedStopCount == 1 ? "1 tramo" : $"{confirmedStopCount} tramos";
        trustLedger.StageEntry(
            TrustLedgerSubjects.User,
            uid,
            acc.TrustScore - prev,
            acc.TrustScore,
            $"Retiro como transportista con {tramosTxt} confirmado(s) (demo, {deltaTotal}).");
        return after;
    }

    private async Task<int?> ApplyStoreTrustPenaltyForSellerExpelIfNeededAsync(
        bool apply,
        int confirmedStopCount,
        string? storeId,
        string reasonForLedger,
        CancellationToken cancellationToken)
    {
        if (!apply || confirmedStopCount < 1)
            return null;
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return null;
        var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (storeRow is null)
            return null;
        var unit = RouteSheetEditAckComputation.StoreTrustPenaltyOnSellerExpelConfirmedCarrier;
        var deltaTotal = -unit * confirmedStopCount;
        var prev = storeRow.TrustScore;
        storeRow.TrustScore = Math.Max(-10_000, prev + deltaTotal);
        var r = reasonForLedger.Length > 120 ? reasonForLedger[..120] + "…" : reasonForLedger;
        var tramosTxt = confirmedStopCount == 1 ? "1 tramo" : $"{confirmedStopCount} tramos";
        trustLedger.StageEntry(
            TrustLedgerSubjects.Store,
            sid,
            storeRow.TrustScore - prev,
            storeRow.TrustScore,
            $"Expulsión de transportista confirmado por la tienda — {tramosTxt} (demo). Motivo: {r}");
        return storeRow.TrustScore;
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
                var parada = SubscriptionsUtils.FindParadaByStopIdOrOrden(
                    payload.Paradas,
                    sub.StopId,
                    sub.StopOrden);
                if (parada is not null)
                {
                    parada.TelefonoTransportista = null;
                    parada.TransportInvitedStoreServiceId = null;
                    parada.TransportInvitedServiceSummary = null;
                }
            }
            RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        }
    }

    private async Task ApplyConfirmedCarriersAsync(
        string threadId,
        string routeSheetId,
        IReadOnlyCollection<string> confirmedStopIds,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || confirmedStopIds.Count == 0)
            return;

        var stopSet = confirmedStopIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var ordered = LogisticsUtils.OrderedStopIds(sheetRow?.Payload);

        var carrierByStop = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.Status == "confirmed"
                && stopSet.Contains(x.StopId))
            .Select(x => new { x.StopId, x.CarrierUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var map = carrierByStop
            .GroupBy(x => (x.StopId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().CarrierUserId ?? "").Trim(), StringComparer.Ordinal);

        var rows = await db.RouteStopDeliveries
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && stopSet.Contains(x.RouteStopId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var agreementIds = rows
            .Select(r => (r.TradeAgreementId ?? "").Trim())
            .Where(x => x.Length >= 8)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var sheetHasIdleCustody = await db.RouteStopDeliveries.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.State == RouteStopDeliveryStates.IdleStoreCustody
                    && x.RefundedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (sheetHasIdleCustody)
            return;

        var siblingRows = agreementIds.Count == 0
            ? []
            : await db.RouteStopDeliveries
                .Where(x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && agreementIds.Contains(x.TradeAgreementId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var byAgreement = siblingRows
            .GroupBy(r => (r.TradeAgreementId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.RouteStopId.Trim(), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.RouteStopId.Trim(), out var carrier) || carrier.Length < 2)
                continue;

            var paidEnough = LogisticsUtils.IsPaidLikeState(row.State);

            var aid = (row.TradeAgreementId ?? "").Trim();
            var byStop = byAgreement.TryGetValue(aid, out var dict) ? dict : new Dictionary<string, RouteStopDeliveryRow>(StringComparer.Ordinal);

            // Igual que tras el cobro: solo el primer tramo pagado (en orden de hoja) recibe titular aquí;
            // los siguientes quedan sin titular hasta cesión / handoff (no adelantar ownership al confirmar carrier).
            var paidStopIds = byStop.Values
                .Where(r =>
                    !string.Equals(r.State, RouteStopDeliveryStates.Unpaid, StringComparison.OrdinalIgnoreCase)
                    && !RouteStopDeliveryStates.IsRefundedTerminal(r.State))
                .Select(r => r.RouteStopId.Trim())
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            var firstPaidStopId = LogisticsUtils.FirstPaidStopId(ordered, paidStopIds);
            var mayGrantOperationalOwner =
                firstPaidStopId is not null
                && string.Equals(row.RouteStopId.Trim(), firstPaidStopId, StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(row.CurrentOwnerUserId)
                && mayGrantOperationalOwner
                && (row.State == RouteStopDeliveryStates.AwaitingCarrierForHandoff || paidEnough))
            {
                row.CurrentOwnerUserId = carrier;
                row.OwnershipGrantedAtUtc = now;
                row.UpdatedAtUtc = now;
                row.State = RouteStopDeliveryStates.AwaitingCarrierForHandoff;

                db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
                {
                    Id = "coe_" + Guid.NewGuid().ToString("N"),
                    ThreadId = tid,
                    RouteSheetId = rsid,
                    RouteStopId = row.RouteStopId,
                    CarrierUserId = carrier,
                    Action = CarrierOwnershipActions.Granted,
                    AtUtc = now,
                    Reason = "carrier_confirmed",
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
