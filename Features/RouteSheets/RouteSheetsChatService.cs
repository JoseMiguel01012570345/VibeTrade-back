using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets;

public sealed class RouteSheetChatService(
    AppDbContext db,
    IChatService chat,
    ITrustScoreLedgerService trustLedger,
    IDrivingLegRoutingService drivingLegRouting,
    ILogger<RouteSheetChatService> logger,
    IRouteSheetThreadNotificationService routeSheetThreadNotifications) : IRouteSheetChatService
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

    public async Task<RouteSheetPayload?> GetPreselPreviewForCarrierAsync(
        string carrierUserId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
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
        var matched = false;
        foreach (var p in payload.Paradas)
        {
            var d = DigitsOnly(p.TelefonoTransportista);
            if (d.Length >= 6 && string.Equals(d, carrierDigits, StringComparison.Ordinal))
            {
                matched = true;
                break;
            }
        }
        if (!matched)
            return null;

        var json = JsonSerializer.Serialize(payload, RouteSheetJson.Options);
        var copy = JsonSerializer.Deserialize<RouteSheetPayload>(json, RouteSheetJson.Options);
        if (copy is null)
            return null;
        copy.Id = rsid;
        copy.ThreadId = tid;
        return copy;
    }

    public Task<bool> RouteSheetIsLockedByPaidAgreementAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
        => RouteSheetHasPaidAgreementLinkAsync(threadId, routeSheetId, cancellationToken);

    private async Task<bool> RouteSheetHasPaidAgreementLinkAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rs = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rs.Length < 1)
            return false;

        var agrIds = await db.TradeAgreements.AsNoTracking()
            .Where(a => a.ThreadId == tid && a.DeletedAtUtc == null && a.RouteSheetId == rs)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        if (agrIds.Count == 0)
            return false;
        return await db.AgreementCurrencyPayments.AsNoTracking()
            .AnyAsync(
                p => agrIds.Contains(p.TradeAgreementId) && p.Status == AgreementPaymentStatuses.Succeeded,
                cancellationToken);
    }

    public async Task<RouteSheetMutationResult> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default)
    {
        var load = await LoadUpsertStateOrFailureAsync(userId, threadId, routeSheetId, payload, cancellationToken);
        if (load.Error is { } err)
            return err;

        var state = load.State!;
        var merge = await MergeUpsertPayloadAsync(threadId, state.RouteSheetId, payload, state, cancellationToken);
        var finalized = await FinalizePersistedUpsertPayloadAsync(merge.Merged, cancellationToken);
        if (finalized.Failure is { } fail)
            return fail;

        var persisted = finalized.Persisted!;
        if (merge.Published && !merge.WasPublishedOnRow
            && !await AgreementLinksRouteSheetAsync(threadId, state.RouteSheetId, cancellationToken)
                .ConfigureAwait(false))
            return RouteSheetMutationResult.PublishRequiresAgreementLink;

        await CompleteUpsertPersistenceAsync(state, merge, persisted, userId, cancellationToken);

        if (state.WasExistingSheet)
            await routeSheetThreadNotifications.PostRouteSheetUpsertEditSystemNoticeAsync(
                userId,
                threadId,
                state.OldSnapshot,
                persisted,
                merge.NextAck,
                merge.AffectedForNotice,
                merge.ConfirmedRowsForNotice,
                cancellationToken);

        if (state.WasExistingSheet
            && RouteSheetEditAckComputation.HasPendingCarrierAck(persisted.RouteSheetEditAck))
            await routeSheetThreadNotifications.BroadcastRouteSheetEditPendingAsync(
                userId,
                threadId,
                state.RouteSheetId,
                cancellationToken);

        return RouteSheetMutationResult.Ok;
    }

    private sealed record UpsertLoadState(
        ChatThreadRow Thread,
        string RouteSheetId,
        ChatRouteSheetRow? Row,
        RouteSheetPayload? OldSnapshot,
        bool WasExistingSheet);

    private sealed record UpsertMergeState(
        RouteSheetPayload Merged,
        List<RouteTramoSubscriptionRow>? SubsForSheet,
        RouteSheetEditAckPayload? NextAck,
        HashSet<string>? AffectedForNotice,
        List<RouteTramoSubscriptionRow>? ConfirmedRowsForNotice,
        bool Published,
        DateTimeOffset Now,
        bool WasPublishedOnRow);

    private readonly record struct UpsertLoadResult(RouteSheetMutationResult? Error, UpsertLoadState? State);

    private readonly record struct UpsertFinalizeResult(RouteSheetMutationResult? Failure, RouteSheetPayload? Persisted);

    private async Task<UpsertLoadResult> LoadUpsertStateOrFailureAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(userId, t))
            return new UpsertLoadResult(RouteSheetMutationResult.NotFoundOrForbidden, null);
        if (!string.Equals((t.SellerUserId ?? "").Trim(), (userId ?? "").Trim(), StringComparison.Ordinal))
            return new UpsertLoadResult(RouteSheetMutationResult.NotFoundOrForbidden, null);

        var rsId = (routeSheetId ?? "").Trim();
        if (rsId.Length == 0)
            return new UpsertLoadResult(RouteSheetMutationResult.NotFoundOrForbidden, null);

        if (await RouteSheetHasPaidAgreementLinkAsync(threadId, rsId, cancellationToken))
            return new UpsertLoadResult(RouteSheetMutationResult.LockedByPaidAgreement, null);

        var idInPayload = (payload.Id ?? "").Trim();
        if (idInPayload.Length > 0 && !string.Equals(idInPayload, rsId, StringComparison.Ordinal))
            return new UpsertLoadResult(RouteSheetMutationResult.NotFoundOrForbidden, null);

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

        return new UpsertLoadResult(
            null,
            new UpsertLoadState(t, rsId, row, oldSnapshot, wasExistingSheet));
    }

    private async Task<UpsertMergeState> MergeUpsertPayloadAsync(
        string threadId,
        string rsId,
        RouteSheetPayload payload,
        UpsertLoadState load,
        CancellationToken cancellationToken)
    {
        var merged = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(payload, RouteSheetJson.Options),
                RouteSheetJson.Options)
            ?? payload;
        merged.Id = rsId;
        merged.ThreadId = threadId;
        merged.Paradas ??= new List<RouteStopPayload>();

        List<RouteTramoSubscriptionRow>? subsForSheet = null;
        if (load.WasExistingSheet)
        {
            subsForSheet = await db.RouteTramoSubscriptions
                .Where(x => x.ThreadId == threadId && x.RouteSheetId == rsId)
                .ToListAsync(cancellationToken);
            ApplyConfirmedSubscriptionStoreServicesToParadas(merged, subsForSheet);
        }

        var editAck = ComputeUpsertEditAckNoticeFields(load, merged, rsId, subsForSheet);
        var now = DateTimeOffset.UtcNow;
        return new UpsertMergeState(
            merged,
            subsForSheet,
            editAck.NextAck,
            editAck.AffectedForNotice,
            editAck.ConfirmedRowsForNotice,
            merged.PublicadaPlataforma == true,
            now,
            load.Row?.PublishedToPlatform == true);
    }

    private static (RouteSheetEditAckPayload? NextAck, HashSet<string>? AffectedForNotice, List<RouteTramoSubscriptionRow>? ConfirmedRowsForNotice)
        ComputeUpsertEditAckNoticeFields(
            UpsertLoadState load,
            RouteSheetPayload merged,
            string rsId,
            List<RouteTramoSubscriptionRow>? subsForSheet)
    {
        if (!load.WasExistingSheet || load.OldSnapshot is null || subsForSheet is null)
        {
            merged.RouteSheetEditAck = null;
            return (null, null, null);
        }

        var confirmedOnSheet = subsForSheet
            .Where(x => string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var confirmedIds = RouteSheetEditAckComputation.ConfirmedCarrierIdsForSheet(subsForSheet, rsId);
        var affected = RouteSheetEditAckComputation.AffectedConfirmedCarrierIds(load.OldSnapshot, merged, confirmedOnSheet);
        var nextAck = RouteSheetEditAckComputation.BuildNextEditAck(load.OldSnapshot.RouteSheetEditAck, confirmedIds, affected);
        if (affected.Count == 0
            && confirmedIds.Count > 0
            && load.OldSnapshot.RouteSheetEditAck is not null)
            nextAck = RouteSheetEditAckComputation.AckAfterSaveWhenNoCarrierAffectedByStopEdits(
                load.OldSnapshot.RouteSheetEditAck,
                confirmedIds);
        merged.RouteSheetEditAck = nextAck;
        return (nextAck, affected, confirmedOnSheet);
    }

    private async Task<UpsertFinalizeResult> FinalizePersistedUpsertPayloadAsync(
        RouteSheetPayload merged,
        CancellationToken cancellationToken)
    {
        var persisted = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(merged, RouteSheetJson.Options),
                RouteSheetJson.Options)
            ?? merged;

        await RouteSheetOsrmRoadKmPopulator.ApplyAsync(persisted, drivingLegRouting, logger, cancellationToken);

        if (RouteSheetPayloadValidator.Validate(persisted) is not null)
            return new UpsertFinalizeResult(RouteSheetMutationResult.NotFoundOrForbidden, null);

        return new UpsertFinalizeResult(null, persisted);
    }

    private async Task CompleteUpsertPersistenceAsync(
        UpsertLoadState state,
        UpsertMergeState merge,
        RouteSheetPayload persisted,
        string userId,
        CancellationToken cancellationToken)
    {
        ApplyUpsertRouteSheetRow(state.Row, state.Thread.Id, state.RouteSheetId, persisted, merge.Published, merge.Now);

        if (merge.SubsForSheet is { Count: > 0 })
            ApplyParadaInvitedServicesToSubscriptions(persisted, merge.SubsForSheet, merge.Now);

        await SyncEmergentOfferAsync(
            state.Thread,
            state.RouteSheetId,
            (userId ?? "").Trim(),
            merge.Published,
            persisted,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void ApplyUpsertRouteSheetRow(
        ChatRouteSheetRow? row,
        string threadId,
        string rsId,
        RouteSheetPayload persisted,
        bool published,
        DateTimeOffset now)
    {
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
            return;
        }

        row.Payload = persisted;
        row.PublishedToPlatform = published;
        row.UpdatedAtUtc = now;
        if (row.DeletedAtUtc is not null)
        {
            row.DeletedAtUtc = null;
            row.DeletedByUserId = null;
        }
    }

    private Task<bool> AgreementLinksRouteSheetAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rs = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rs.Length < 1)
            return Task.FromResult(false);

        // Avoid string.Equals(..., StringComparison): not translatable to SQL on all EF providers.
        return db.TradeAgreements.AsNoTracking().AnyAsync(
            a =>
                a.ThreadId == tid
                && a.DeletedAtUtc == null
                && (a.RouteSheetId ?? "") == rs,
            cancellationToken);
    }

    public async Task<RouteSheetMutationResult> DeleteAsync(
        string userId,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(userId, t))
            return RouteSheetMutationResult.NotFoundOrForbidden;
        if (!string.Equals((t.SellerUserId ?? "").Trim(), (userId ?? "").Trim(), StringComparison.Ordinal))
            return RouteSheetMutationResult.NotFoundOrForbidden;

        var rsId = (routeSheetId ?? "").Trim();
        if (rsId.Length == 0)
            return RouteSheetMutationResult.NotFoundOrForbidden;

        var row = await db.ChatRouteSheets.FirstOrDefaultAsync(
            x => x.ThreadId == threadId && x.RouteSheetId == rsId,
            cancellationToken);
        if (row is null)
            return RouteSheetMutationResult.NotFoundOrForbidden;

        if (row.DeletedAtUtc is not null)
            return RouteSheetMutationResult.Ok;

        if (await RouteSheetHasPaidAgreementLinkAsync(threadId, rsId, cancellationToken))
            return RouteSheetMutationResult.LockedByPaidAgreement;

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

        int? storeTrustBalanceAfterDelete = null;
        int? storeTrustDeltaDelete = null;
        if (nConfirmed > 0)
        {
            var storeId = (t.StoreId ?? "").Trim();
            if (storeId.Length >= 2)
            {
                var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken);
                if (storeRow is not null)
                {
                    var delta = -RouteSheetEditAckComputation.StoreTrustPenaltyPerConfirmedCarrierOnSheetDelete
                        * nConfirmed;
                    storeRow.TrustScore = Math.Max(-10_000, storeRow.TrustScore + delta);
                    trustLedger.StageEntry(
                        TrustLedgerSubjects.Store,
                        storeId,
                        delta,
                        storeRow.TrustScore,
                        $"Eliminación de hoja con transportistas confirmados ({nConfirmed}×, demo)");
                    storeTrustBalanceAfterDelete = storeRow.TrustScore;
                    storeTrustDeltaDelete = delta;
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

        await routeSheetThreadNotifications.NotifyAfterRouteSheetDeletedAsync(
            userId.Trim(),
            threadId,
            rsId,
            t.SellerUserId,
            t.OfferId,
            row.Payload.Titulo,
            nConfirmed,
            subs.Count,
            storeTrustBalanceAfterDelete,
            storeTrustDeltaDelete,
            emergentPubId,
            cancellationToken);

        return RouteSheetMutationResult.Ok;
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
                && ChatThreadAccess.UserIdsMatchLoose(cid, x.CarrierUserId)))
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
            subs = subs.Where(x => ChatThreadAccess.UserIdsMatchLoose(cid, x.CarrierUserId)).ToList();
            foreach (var s in subs)
            {
                s.Status = "withdrawn";
                s.UpdatedAtUtc = now;
            }

            PersistSheetPayloadWithAck(sheetRow, ack, now, p => ClearTransportistaPhonesForSubs(p, subs));
            var balReject = await ApplyStoreTrustPenaltyOnSheetEditRejectAsync(thread.StoreId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (balReject is int balR)
            {
                var sellerNotify = (thread.SellerUserId ?? "").Trim();
                if (sellerNotify.Length >= 2)
                {
                    await routeSheetThreadNotifications.NotifySellerStoreTrustPenaltyAfterSheetEditRejectAsync(
                        sellerNotify,
                        tid,
                        (thread.OfferId ?? "").Trim(),
                        balR,
                        cancellationToken);
                }
            }
            await routeSheetThreadNotifications.PostAutomatedSheetEditCarrierResponseNoticeAsync(
                tid,
                accepted: false,
                carrierName,
                sheetTitle,
                cancellationToken);
        }
        else
        {
            PersistSheetPayloadWithAck(sheetRow, ack, now);
            await db.SaveChangesAsync(cancellationToken);
            await routeSheetThreadNotifications.PostAutomatedSheetEditCarrierResponseNoticeAsync(
                tid,
                accepted: true,
                carrierName,
                sheetTitle,
                cancellationToken);
        }

        await routeSheetThreadNotifications.BroadcastRouteTramoSubscriptionsSheetEditCarrierResponseAsync(
            tid,
            rsid,
            accept,
            cid,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Notificaciones a teléfonos que resuelven a cuentas. <c>-1</c> = sin acceso a hilo/hoja o datos inválidos;
    /// <c>0</c> = permitido pero nadie a quien notificar; <c>&gt;0</c> = avisos enviados.
    /// </summary>
    public async Task<int> NotifyPreselectedTransportistasAsync(
        string editorUserId,
        string threadId,
        string routeSheetId,
        IReadOnlyList<RouteSheetPreselectedInvite> invites,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var eid = (editorUserId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || eid.Length < 2)
            return -1;
        if (invites is null || invites.Count == 0)
            return -1;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null
            || thread.DeletedAtUtc is not null
            || !ChatThreadAccess.UserCanSeeThread(eid, thread))
            return -1;
        if (!string.Equals((thread.SellerUserId ?? "").Trim(), eid, StringComparison.Ordinal))
            return -1;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null)
            return -1;

        var restoredCarrierContactOnStop = false;

        var title = TruncateRouteSheetTitle(sheetRow.Payload.Titulo);
        var offerId = (thread.OfferId ?? "").Trim();
        if (offerId.Length < 2)
            return -1;

        var editor = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid, cancellationToken);
        if (editor is null)
            return -1;
        var authorLabel = (editor.DisplayName ?? "").Trim();
        if (authorLabel.Length == 0)
            authorLabel = "Participante";
        var authorTrust = editor.TrustScore;

        var messageBase = title.Length > 0
            ? $"Te indicaron como contacto de transporte en la hoja de ruta «{title}». Abrí la notificación para ver el mapa, la tarifa y aceptar o rechazar la invitación."
            : "Te indicaron como contacto de transporte en una hoja de ruta. Abrí la notificación para ver el mapa, la tarifa y aceptar o rechazar la invitación.";

        var subsForSheet = await db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid)
            .ToListAsync(cancellationToken);

        var byRecipient = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var inv in invites)
        {
            var stopId = (inv.StopId ?? "").Trim();
            if (stopId.Length < 1)
                continue;
            var inviteDigits = DigitsOnly(inv.Phone);
            if (inviteDigits.Length < 6)
                continue;

            var parada = sheetRow.Payload.Paradas?
                .FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), stopId, StringComparison.Ordinal));
            if (parada is null)
                continue;

            var acc = await db.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PhoneDigits == inviteDigits, cancellationToken);
            if (acc is null)
                continue;
            var uid = (acc.Id ?? "").Trim();
            if (uid.Length < 2 || string.Equals(uid, eid, StringComparison.Ordinal))
                continue;

            var onSheetDigits = DigitsOnly(parada.TelefonoTransportista);
            var inviteMatchesSheetPhone =
                string.Equals(onSheetDigits, inviteDigits, StringComparison.Ordinal);
            if (!inviteMatchesSheetPhone)
            {
                // Tras <see cref="VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionService.PreselExecuteRejectAsync"/> el teléfono se borra del tramo;
                // el vendedor debe poder reenviar presel al mismo contacto (lista stopId + teléfono).
                var canReinviteAfterDecline = subsForSheet.Exists(s =>
                    string.Equals((s.StopId ?? "").Trim(), stopId, StringComparison.Ordinal)
                    && ChatThreadAccess.UserIdsMatchLoose(uid, s.CarrierUserId)
                    && PreselInviteEligibleAfterSheetPhoneCleared(s.Status));
                var sheetPhoneCleared = onSheetDigits.Length < 6;
                if (!canReinviteAfterDecline && !sheetPhoneCleared)
                    continue;
            }

            // Sin teléfono en el tramo (p. ej. tras rechazo presel) el transportista no puede aceptar:
            // restauramos el contacto indicado por el vendedor en el mismo POST de aviso.
            if (onSheetDigits.Length < 6 && inviteDigits.Length >= 6)
            {
                var tel = (inv.Phone ?? "").Trim();
                if (tel.Length > 0)
                {
                    parada.TelefonoTransportista = tel;
                    restoredCarrierContactOnStop = true;
                }
            }

            if (!byRecipient.TryGetValue(uid, out var stopSet))
            {
                stopSet = new HashSet<string>(StringComparer.Ordinal);
                byRecipient[uid] = stopSet;
            }

            stopSet.Add(stopId);
        }

        if (restoredCarrierContactOnStop)
        {
            RouteSheetPayloadPersistence.ApplyPayloadAndTouch(sheetRow, sheetRow.Payload, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        var n = 0;
        foreach (var kv in byRecipient)
        {
            var uid = kv.Key;
            var stopIdsForRecipient = kv.Value.ToList();
            if (stopIdsForRecipient.Count == 0)
                continue;
            var stopsToNotify = stopIdsForRecipient
                .Where(sid => !ShouldSkipPreselectedNotifyForSingleStop(
                    sheetRow.Payload,
                    uid,
                    sid,
                    subsForSheet))
                .ToList();
            if (stopsToNotify.Count == 0)
                continue;
            await routeSheetThreadNotifications.NotifyRouteSheetPreselectedTransportistaAsync(
                new RouteSheetPreselectedTransportistaNotificationArgs(
                    uid,
                    tid,
                    offerId,
                    rsid,
                    messageBase,
                    authorLabel,
                    authorTrust,
                    eid,
                    stopsToNotify),
                cancellationToken);
            ApplyStopContentFingerprintsAfterPreselectedNotify(
                sheetRow.Payload,
                subsForSheet,
                uid,
                stopsToNotify);
            n++;
        }

        if (n > 0)
            await db.SaveChangesAsync(cancellationToken);

        return n;
    }

    /// <summary>Rechazo/retiro: el vendedor puede volver a notificar aunque el tramo ya no tenga el teléfono en la hoja.</summary>
    private static bool PreselInviteEligibleAfterSheetPhoneCleared(string? status)
    {
        var st = (status ?? "").Trim().ToLowerInvariant();
        return st is "rejected" or "withdrawn";
    }

    /// <summary>
    /// <c>true</c> si este tramo ya está cubierto (suscripción pending/confirmed + mismo fingerprint) y no hace falta otro aviso presel.
    /// </summary>
    private static bool ShouldSkipPreselectedNotifyForSingleStop(
        RouteSheetPayload payload,
        string recipientUserId,
        string stopId,
        IReadOnlyList<RouteTramoSubscriptionRow> subsForSheet)
    {
        var sid = (stopId ?? "").Trim();
        if (sid.Length == 0)
            return false;

        var parada = payload.Paradas?
            .FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
        if (parada is null)
            return false;

        var fp = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
        var sub = subsForSheet.FirstOrDefault(s =>
            string.Equals((s.StopId ?? "").Trim(), sid, StringComparison.Ordinal)
            && ChatThreadAccess.UserIdsMatchLoose(recipientUserId, s.CarrierUserId));
        if (sub is null)
            return false;

        var st = (sub.Status ?? "").Trim().ToLowerInvariant();
        if (st is "rejected" or "withdrawn")
            return false;
        if (st is "pending" or "confirmed")
            return string.Equals(fp, sub.StopContentFingerprint ?? "", StringComparison.Ordinal);

        return false;
    }

    private static void ApplyStopContentFingerprintsAfterPreselectedNotify(
        RouteSheetPayload payload,
        List<RouteTramoSubscriptionRow> subsForSheet,
        string recipientUserId,
        IReadOnlyList<string> stopIdsForRecipient)
    {
        foreach (var stopId in stopIdsForRecipient)
        {
            var sid = (stopId ?? "").Trim();
            if (sid.Length == 0)
                continue;
            var parada = payload.Paradas?
                .FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (parada is null)
                continue;
            var fp = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
            foreach (var sub in subsForSheet)
            {
                if (!string.Equals((sub.StopId ?? "").Trim(), sid, StringComparison.Ordinal))
                    continue;
                if (!ChatThreadAccess.UserIdsMatchLoose(recipientUserId, sub.CarrierUserId))
                    continue;
                sub.StopContentFingerprint = fp;
            }
        }
    }

    private static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return string.Concat(raw.Where(char.IsDigit));
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
            if (ChatThreadAccess.UserIdsMatchLoose(v, kv.Key))
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

    /// <summary>
    /// Si el PUT no trae <c>transportInvitedStoreServiceId</c> en un tramo ya confirmado, rellenamos desde la suscripción.
    /// Si el cliente envía un id, no lo pisamos con el valor viejo de BD.
    /// </summary>
    private static void ApplyConfirmedSubscriptionStoreServicesToParadas(
        RouteSheetPayload payload,
        IReadOnlyList<RouteTramoSubscriptionRow> subs)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            if (!string.Equals((sub.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            var storeSvc = (sub.StoreServiceId ?? "").Trim();
            if (storeSvc.Length == 0)
                continue;
            var parada = TryResolveParadaForSubscription(payload, sub);
            if (parada is null)
                continue;
            var incomingInvited = (parada.TransportInvitedStoreServiceId ?? "").Trim();
            if (incomingInvited.Length > 0)
                continue;
            parada.TransportInvitedStoreServiceId = storeSvc;
            var label = (sub.TransportServiceLabel ?? "").Trim();
            if (label.Length > 0)
                parada.TransportInvitedServiceSummary = label;
        }
    }

    /// <summary>
    /// Alinea <see cref="RouteTramoSubscriptionRow.StoreServiceId"/> (y etiqueta) con el payload guardado en la hoja.
    /// </summary>
    private static void ApplyParadaInvitedServicesToSubscriptions(
        RouteSheetPayload payload,
        List<RouteTramoSubscriptionRow> subs,
        DateTimeOffset now)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            var parada = TryResolveParadaForSubscription(payload, sub);
            if (parada is null)
                continue;
            var inv = (parada.TransportInvitedStoreServiceId ?? "").Trim();
            var sum = (parada.TransportInvitedServiceSummary ?? "").Trim();
            sub.StoreServiceId = inv.Length > 0 ? inv : null;
            sub.TransportServiceLabel = sum;
            sub.UpdatedAtUtc = now;
        }
    }

    private static RouteStopPayload? TryResolveParadaForSubscription(
        RouteSheetPayload payload,
        RouteTramoSubscriptionRow sub)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        var stopId = (sub.StopId ?? "").Trim();
        if (stopId.Length > 0)
        {
            var byId = payload.Paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), stopId, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }
        if (sub.StopOrden > 0)
            return payload.Paradas.FirstOrDefault(p => p.Orden == sub.StopOrden);
        return null;
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
            {
                parada.TelefonoTransportista = null;
                parada.TransportInvitedStoreServiceId = null;
                parada.TransportInvitedServiceSummary = null;
            }
        }
    }

    private async Task<int?> ApplyStoreTrustPenaltyOnSheetEditRejectAsync(
        string? storeId,
        CancellationToken cancellationToken)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return null;
        var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (storeRow is null)
            return null;
        var delta = -RouteSheetEditAckComputation.StoreTrustPenaltyOnCarrierRejectSheetEdit;
        storeRow.TrustScore = Math.Max(-10_000, storeRow.TrustScore + delta);
        trustLedger.StageEntry(
            TrustLedgerSubjects.Store,
            sid,
            delta,
            storeRow.TrustScore,
            "Transportista rechazó cambios en la hoja de ruta");
        return storeRow.TrustScore;
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
