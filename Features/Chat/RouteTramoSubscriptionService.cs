using System.Text.Json;
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

    /// <summary>Texto en hoja / suscripción cuando la invitación presel no liga una ficha de catálogo.</summary>
    private const string PreselHojaServicioTransporte = "Servicio de transporte";

    /// <summary>Marca en la hoja: el teléfono del tramo proviene de la invitación por contacto en la hoja.</summary>
    private const string PreselHojaContactoIndicado = "Contacto indicado en la hoja";

    private static readonly JsonSerializerOptions TramoAcceptedMetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Meta para notificación <c>route_tramo_subscribe_accepted</c> y enriquecimiento de UI (enlace a ficha).</summary>
    private static string? BuildTramoAcceptedNotificationMetaJson(
        string routeSheetId,
        string carrierUserId,
        IReadOnlyList<(string StopId, string? StoreServiceId)> stops)
    {
        if (stops.Count == 0)
            return null;
        var rs = (routeSheetId ?? "").Trim();
        var cu = (carrierUserId ?? "").Trim();
        if (rs.Length < 1 || cu.Length < 2)
            return null;
        var stopObjs = stops
            .Select(t => new
            {
                stopId = (t.StopId ?? "").Trim(),
                storeServiceId = string.IsNullOrWhiteSpace(t.StoreServiceId) ? null : t.StoreServiceId.Trim(),
            })
            .Where(x => x.stopId.Length > 0)
            .ToList();
        if (stopObjs.Count == 0)
            return null;
        var payload = new
        {
            routeSheetId = rs,
            carrierUserId = cu,
            stops = stopObjs,
        };
        return JsonSerializer.Serialize(payload, TramoAcceptedMetaJsonOptions);
    }

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
        var narrowToCarrierOnly = !ChatThreadAccess.UserCanSeeThread(uid, thread);

        var publishedSheets = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null && x.PublishedToPlatform)
            .ToListAsync(cancellationToken);

        var sheetsById = publishedSheets
            .ToDictionary(x => x.RouteSheetId, x => x, StringComparer.Ordinal);

        // Transportista sin Initiator/FirstMessage: puede tener suscripción (p. ej. presel) en hoja aún no publicada;
        // sin esto GET devuelve [] y el cliente bloquea el chat aunque UserCanAccessThreadRowAsync sea true.
        if (narrowToCarrierOnly)
        {
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

        var acceptedMeta =
            BuildTramoAcceptedNotificationMetaJson(k.RouteSheetId, k.CarrierId, metaStops);

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
                carrierAcc?.TrustScore ?? 0,
                acceptedMeta),
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
            $"{sellerLabel} rechazó tu solicitud de transporte en un tramo de la hoja de ruta publicada. Puedes revisar la oferta y los tramos disponibles.";

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
        await ClearCarrierPhoneOnSheetsForWithdrawAsync(ctx.ThreadId, ctx.Subs, now, cancellationToken);
        MarkSubscriptionsWithdrawn(ctx.Subs, now);
        int? storeTrustAfter = await ApplyStoreTrustPenaltyForSellerExpelIfNeededAsync(
            ctx.ApplyStoreTrustPenalty,
            ctx.ConfirmedStopsWithdrawnCount,
            ctx.Thread.StoreId,
            ctx.ReasonTrim,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (ctx.ApplyStoreTrustPenalty && storeTrustAfter is int balAfter)
            await NotifySellerStorePenaltyAfterExpelAsync(ctx, balAfter, cancellationToken);

        await BroadcastSellerExpelSideEffectsAsync(ctx, cancellationToken);

        return new CarrierExpelledBySellerResult(
            ctx.Subs.Count,
            ctx.ApplyStoreTrustPenalty,
            storeTrustAfter,
            ctx.ConfirmedStopsWithdrawnCount,
            ctx.CarrierFullyRemovedFromThread);
    }

    private sealed record SellerExpelContext(
        ChatThreadRow Thread,
        string SellerUserId,
        string ThreadId,
        string CarrierUserId,
        string ReasonTrim,
        bool ExpelSingleTramo,
        List<RouteTramoSubscriptionRow> Subs,
        int ConfirmedStopsWithdrawnCount,
        bool CarrierFullyRemovedFromThread,
        bool ApplyStoreTrustPenalty,
        IReadOnlyList<string> DistinctRouteSheetIds);

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
        if (subs.Count == 0)
            return null;

        if (expelSingleTramo)
            subs = subs.Where(x => x.RouteSheetId == filterRs && x.StopId == filterStop).ToList();
        if (subs.Count == 0)
            return null;

        var hadConfirmed = subs.Exists(x =>
            string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));
        if (!hadConfirmed)
            return null;

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
            && thread.SellerExpelledAtUtc is null;

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

    private async Task NotifySellerStorePenaltyAfterExpelAsync(
        SellerExpelContext ctx,
        int balanceAfter,
        CancellationToken cancellationToken)
    {
        var unit = RouteSheetEditAckComputation.StoreTrustPenaltyOnSellerExpelConfirmedCarrier;
        var deltaPenalty = -unit * ctx.ConfirmedStopsWithdrawnCount;
        var previewPenalty = ctx.ConfirmedStopsWithdrawnCount <= 1 ?
            "Expulsaste a un transportista confirmado; se aplicó un ajuste de confianza a tu tienda (demo)."
            : $"Expulsaste a un transportista confirmado ({ctx.ConfirmedStopsWithdrawnCount} tramos); se aplicaron varios ajustes de confianza a tu tienda (demo).";
        await chat.NotifySellerStoreTrustPenaltyAsync(
            new SellerStoreTrustPenaltyNotificationArgs(
                ctx.SellerUserId,
                ctx.ThreadId,
                (ctx.Thread.OfferId ?? "").Trim(),
                deltaPenalty,
                balanceAfter,
                previewPenalty),
            cancellationToken);
    }

    private async Task BroadcastSellerExpelSideEffectsAsync(
        SellerExpelContext ctx,
        CancellationToken cancellationToken)
    {
        var tid = ctx.ThreadId;
        var sid = ctx.SellerUserId;
        var carrierId = ctx.CarrierUserId;
        var reasonTrim = ctx.ReasonTrim;

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ctx.Thread.StoreId, cancellationToken);
        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        var (sellerLabel, sellerTrust) = RouteTramoSellerPresentation.LabelAndTrust(store, actorAcc);
        var carrierName = (await db.UserAccounts.AsNoTracking()
            .Where(x => x.Id == carrierId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken))?.Trim() ?? "Transportista";

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == ctx.DistinctRouteSheetIds[0]
                    && x.RetractedAtUtc == null,
                cancellationToken);
        var routeOfferId = string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();

        var preview = ctx.CarrierFullyRemovedFromThread ?
            $"La tienda te retiró de esta operación. Motivo: {reasonTrim}"
            : $"La tienda te retiró de un tramo de esta operación. Motivo: {reasonTrim}";

        await chat.NotifyRouteTramoSellerExpelledAsync(
            new RouteTramoSellerExpelledNotificationArgs(
                carrierId,
                tid,
                preview,
                sellerLabel,
                sellerTrust,
                sid,
                routeOfferId,
                reasonTrim),
            cancellationToken);

        var sys = ctx.ExpelSingleTramo && !ctx.CarrierFullyRemovedFromThread ?
            $"{sellerLabel} retiró a {carrierName} de un tramo de la oferta de ruta. Motivo: {reasonTrim}."
            : $"{sellerLabel} retiró a {carrierName} de la oferta de ruta. Motivo: {reasonTrim}.";
        await chat.PostAutomatedSystemThreadNoticeAsync(tid, sys, cancellationToken);

        foreach (var rsid in ctx.DistinctRouteSheetIds)
        {
            var emergentPubId = await EmergentIdForThreadSheetAsync(tid, rsid, cancellationToken);
            await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "withdraw", sid, emergentPubId),
                cancellationToken);
        }
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
        if (thread.BuyerExpelledAtUtc is not null || thread.SellerExpelledAtUtc is not null)
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

    private sealed record PreselCore(
        ChatThreadRow Thread,
        UserAccount Carrier,
        RouteSheetPayload Payload,
        string Rsid);

    public async Task<bool> CarrierRespondPreselectedRouteInviteAsync(
        CarrierPreselInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        var core = await PreselLoadCoreAsync(request, cancellationToken);
        if (core is null)
            return false;
        var digits = (core.Carrier.PhoneDigits ?? "").Trim();
        var stops = PreselMatchStops(core.Payload, digits, request.StopIdRestrict);

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

    private static List<RouteStopPayload> PreselMatchStops(
        RouteSheetPayload payload,
        string carrierDigits,
        string? stopIdRestrict)
    {
        var stopRestrict = (stopIdRestrict ?? "").Trim();
        var list = new List<RouteStopPayload>();
        foreach (var p in payload.Paradas ?? [])
        {
            var d = DigitsOnlyTel(p.TelefonoTransportista);
            if (d.Length < 6 || !string.Equals(d, carrierDigits, StringComparison.Ordinal))
                continue;
            if (stopRestrict.Length > 0
                && !string.Equals((p.Id ?? "").Trim(), stopRestrict, StringComparison.Ordinal))
                continue;
            list.Add(p);
        }
        return list;
    }

    private async Task<(string? StoreServiceId, string TransportServiceLabel)> ResolvePreselInvitedStoreServiceAsync(
        string carrierUserId,
        RouteStopPayload? stop,
        CancellationToken cancellationToken)
    {
        var reqId = (stop?.TransportInvitedStoreServiceId ?? "").Trim();
        if (reqId.Length < 2)
            return (null, PreselHojaServicioTransporte);

        var row = await db.StoreServices.AsNoTracking()
            .Include(s => s.Store)
            .FirstOrDefaultAsync(s => s.Id == reqId, cancellationToken);
        // Mismo criterio que el catálogo: null = publicado; solo false oculta.
        if (row is null || row.DeletedAtUtc is not null)
            return (null, PreselHojaServicioTransporte);
        var owner = (row.Store?.OwnerUserId ?? "").Trim();
        if (!ChatThreadAccess.UserIdsMatchLoose(carrierUserId, owner))
            return (null, PreselHojaServicioTransporte);
        
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

    private static void ApplyPreselAcceptedFieldsToParadaHoja(
        RouteStopPayload parada,
        string? invSvc,
        string transportServiceLabel)
    {
        parada.TransportInvitedServiceSummary = transportServiceLabel;
        if (string.IsNullOrWhiteSpace(invSvc))
            parada.TransportInvitedStoreServiceId = null;

        var notas = (parada.Notas ?? "").Trim();
        if (notas.Contains(PreselHojaContactoIndicado, StringComparison.OrdinalIgnoreCase))
            return;
        parada.Notas = notas.Length == 0
            ? PreselHojaContactoIndicado
            : $"{PreselHojaContactoIndicado}. {notas}";
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

        var phoneSnapRaw = PhoneSnap40(core.Carrier);
        var phoneSnap = phoneSnapRaw.Length > 0 ? phoneSnapRaw : null;
        var accountPhone = RouteTramoUserContactUtil.BestPhoneForCarrier(core.Carrier, null, null);

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
                ApplyPreselAcceptedFieldsToParadaHoja(stop, invSvc, invLabel);
        }

        RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, payload, now);
        await db.SaveChangesAsync(cancellationToken);

        var emergentPubId = await EmergentIdForThreadSheetAsync(tid, rsid, cancellationToken);

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sellerId, cancellationToken);
        var deciderLabel = RouteTramoUserContactUtil.ParticipanteOrDisplay(actorAcc?.DisplayName);
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview =
            $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var carrierLabel = string.IsNullOrWhiteSpace(core.Carrier.DisplayName)
            ? "El transportista"
            : core.Carrier.DisplayName.Trim();
        var sellerInboxPreview =
            $"Confirmaste el servicio de transporte de {carrierLabel} en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var preselAcceptedMeta =
            BuildTramoAcceptedNotificationMetaJson(rsid, uid, preselMetaStops);

        await chat.NotifyRouteTramoSubscriptionAcceptedAsync(
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
            cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(tid, rsid, "accept", sellerId, emergentPubId),
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
                var parada = RouteTramoParadaResolver.FindByStopIdOrOrden(
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

            var emergentPubId = await EmergentIdForThreadSheetAsync(tid, rsid, cancellationToken);
            await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                new RouteTramoSubscriptionsBroadcastArgs(
                    tid,
                    rsid,
                    "presel_decline",
                    uid,
                    emergentPubId),
                cancellationToken);
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

        await chat.NotifyRouteSheetPreselDeclinedByCarrierAsync(
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

    private static string PhoneSnap40(UserAccount carrier)
    {
        var phoneSnap = (carrier.PhoneDisplay ?? "").Trim();
        if (phoneSnap.Length == 0 && !string.IsNullOrWhiteSpace(carrier.PhoneDigits))
            phoneSnap = carrier.PhoneDigits.Trim();
        if (phoneSnap.Length > 40)
            phoneSnap = phoneSnap[..40];
        return phoneSnap;
    }

    private static string DigitsOnlyTel(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
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
                {
                    parada.TelefonoTransportista = null;
                    parada.TransportInvitedStoreServiceId = null;
                    parada.TransportInvitedServiceSummary = null;
                }
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
