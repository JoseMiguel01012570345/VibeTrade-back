using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.RouteSheets.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.Search.Interfaces;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.RouteSheets;

public sealed class RouteSheetChatService(
    AppDbContext db,
    IChatService chat,
    ITrustScoreLedgerService trustLedger,
    IDrivingLegRoutingService drivingLegRouting,
    ILogger<RouteSheetChatService> logger,
    IRouteSheetThreadNotificationService routeSheetThreadNotifications,
    ICatalogSearchLiveIndexSync catalogSearchLiveIndex) : IRouteSheetChatService
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
            var d = AuthUtils.DigitsOnly(p.TelefonoTransportista);
            if (d.Length >= 6 && string.Equals(d, carrierDigits, StringComparison.Ordinal))
            {
                matched = true;
                break;
            }
        }
        if (!matched)
            return null;

        var copy = RouteSheetUtils.ClonePayload(payload);
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

    /// <inheritdoc />
    public async Task<HashSet<string>> LoadConfirmedRouteStopIdsAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (tid.Length < 2 || rsid.Length < 1)
            return set;

        var rows = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.Status == "confirmed")
            .Select(x => x.StopId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in rows)
        {
            var s = (id ?? "").Trim();
            if (s.Length > 0)
                set.Add(s);
        }

        return set;
    }

    /// <summary>Reglas de cobro por ruta enlazada: cada tramo debe tener transportista confirmado.</summary>
    public static bool AllStopsHaveConfirmedCarrier(
        IReadOnlyList<RouteStopPayload> stops,
        IReadOnlySet<string> confirmedStopIds) =>
        stops.All(p =>
        {
            var sid = (p.Id ?? "").Trim();
            return sid.Length == 0 || confirmedStopIds.Contains(sid);
        });

    public static bool PathMissingConfirmedCarriers(
        RoutePathDto path,
        IReadOnlySet<string> confirmedStopIds) =>
        path.TotalsByCurrency.Count > 0
        && path.StopIds.Any(sid => !confirmedStopIds.Contains((sid ?? "").Trim()));

    /// <summary>
    /// Criterios para marcar entregada y retirar de la plataforma cuando la logística del último tramo quedó liquidada.
    /// </summary>
    public static bool IsTerminalSettledState(string? stateRaw)
    {
        var s = (stateRaw ?? "").Trim();
        return string.Equals(s, RouteStopDeliveryStates.EvidenceAccepted, StringComparison.OrdinalIgnoreCase)
            || RouteStopDeliveryStates.IsRefundedTerminal(s);
    }

    public static bool PayloadMarkedDelivered(RouteSheetPayload? payload) =>
        string.Equals((payload?.Estado ?? "").Trim(), "entregada", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El tramo aceptado es el último de la hoja y ya no quedan entregas activas en ningún acuerdo de esa hoja.
    /// </summary>
    public static bool ShouldAutoArchiveRouteSheet(
        IReadOnlyList<string> orderedStopIds,
        string acceptedStopId,
        IReadOnlyList<string> deliveryStatesOnSheet)
    {
        if (orderedStopIds.Count == 0)
            return false;

        var lastId = orderedStopIds[^1].Trim();
        var sid = (acceptedStopId ?? "").Trim();
        if (sid.Length == 0 || !string.Equals(sid, lastId, StringComparison.Ordinal))
            return false;

        if (deliveryStatesOnSheet.Count == 0)
            return false;

        foreach (var state in deliveryStatesOnSheet)
        {
            var s = (state ?? "").Trim();
            if (string.Equals(s, RouteStopDeliveryStates.Unpaid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsTerminalSettledState(s))
                return false;
        }

        return deliveryStatesOnSheet.Any(s =>
            !string.Equals((s ?? "").Trim(), RouteStopDeliveryStates.Unpaid, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLastOrderedStop(IReadOnlyList<string> orderedStopIds, string stopId) =>
        orderedStopIds.Count > 0
        && string.Equals(orderedStopIds[^1].Trim(), (stopId ?? "").Trim(), StringComparison.Ordinal);

    public async Task<RouteSheetMutationResult> UpsertAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken = default)
    {
        return await UpsertInternalAsync(
            userId,
            threadId,
            routeSheetId,
            payload,
            bypassUnpaidAgreementLimit: false,
            cancellationToken);
    }

    private async Task<RouteSheetMutationResult> UpsertInternalAsync(
        string userId,
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        bool bypassUnpaidAgreementLimit,
        CancellationToken cancellationToken)
    {
        var load = await LoadUpsertStateOrFailureAsync(
            userId,
            threadId,
            routeSheetId,
            payload,
            bypassUnpaidAgreementLimit,
            cancellationToken);
        if (load.Error is { } err)
            return err;

        var state = load.State!;
        var merge = await MergeUpsertPayloadAsync(threadId, state.RouteSheetId, payload, state, cancellationToken);
        var finalized = await FinalizePersistedUpsertPayloadAsync(merge.Merged, cancellationToken);
        if (finalized.Failure is { } fail)
            return fail;

        var persisted = finalized.Persisted!;
        if (await ValidateMerchandiseRouteCurrencyAsync(threadId, state.RouteSheetId, persisted, cancellationToken)
                is { } currencyFail)
            return currencyFail;

        await CompleteUpsertPersistenceAsync(state, merge, persisted, userId, cancellationToken);

        if (merge.Published || (merge.WasPublishedOnRow && !merge.Published))
        {
            var storeId = (state.Thread.StoreId ?? "").Trim();
            if (storeId.Length >= 2)
                await catalogSearchLiveIndex.SyncStoreAsync(storeId, cancellationToken);
        }

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
        bool bypassUnpaidAgreementLimit,
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
            oldSnapshot = RouteSheetUtils.ClonePayload(row.Payload);
        }

        if (await RouteSheetHasPaidAgreementLinkAsync(threadId, rsId, cancellationToken))
        {
            if (oldSnapshot is null || !wasExistingSheet)
                return new UpsertLoadResult(RouteSheetMutationResult.LockedByPaidAgreement, null);

            var confirmedStopIds = await LoadConfirmedRouteStopIdsAsync(threadId, rsId, cancellationToken);
            if (!RouteSheetPaidEditPolicy.IsCarrierContactOnlyUpdate(
                    oldSnapshot,
                    payload,
                    confirmedStopIds)
                && !RouteSheetPaidEditPolicy.IsPublishToggleOnlyUpdate(oldSnapshot, payload))
                return new UpsertLoadResult(RouteSheetMutationResult.LockedByPaidAgreement, null);
        }

        if (!wasExistingSheet && !bypassUnpaidAgreementLimit)
        {
            var unpaidCount = await db.TradeAgreements.AsNoTracking()
                .Where(a => a.ThreadId == threadId
                            && a.DeletedAtUtc == null
                            && a.Status == "accepted")
                .CountAsync(
                    a => !db.AgreementCurrencyPayments.AsNoTracking().Any(
                        p => p.TradeAgreementId == a.Id && p.Status == AgreementPaymentStatuses.Succeeded),
                    cancellationToken);

            var activeSheetCount = await db.ChatRouteSheets.AsNoTracking()
                .CountAsync(x => x.ThreadId == threadId && x.DeletedAtUtc == null, cancellationToken);

            if (activeSheetCount >= unpaidCount)
                return new UpsertLoadResult(RouteSheetMutationResult.ExceedsUnpaidAgreementLimit, null);
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
        var merged = RouteSheetUtils.ClonePayload(payload);
        merged.Id = rsId;
        merged.ThreadId = threadId;
        merged.Paradas ??= new List<RouteStopPayload>();

        List<RouteTramoSubscriptionRow>? subsForSheet = null;
        if (load.WasExistingSheet)
        {
            subsForSheet = await db.RouteTramoSubscriptions
                .Where(x => x.ThreadId == threadId && x.RouteSheetId == rsId)
                .ToListAsync(cancellationToken);
            RouteSheetUtils.ApplyConfirmedSubscriptionStoreServicesToParadas(merged, subsForSheet);
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
        var persisted = RouteSheetUtils.ClonePayload(merged);

        await RouteSheetOsrmRoadKmPopulator.ApplyAsync(persisted, drivingLegRouting, logger, cancellationToken);

        if (RouteSheetUtils.ValidateEstimatedTimes(persisted) is not null)
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
            RouteSheetUtils.ApplyParadaInvitedServicesToSubscriptions(persisted, merge.SubsForSheet, merge.Now);

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

    private async Task<RouteSheetMutationResult?> ValidateMerchandiseRouteCurrencyAsync(
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var rs = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rs.Length < 1)
            return null;

        var ag = await db.TradeAgreements.AsNoTracking()
            .Include(a => a.MerchandiseLines)
            .FirstOrDefaultAsync(
                a =>
                    a.ThreadId == tid
                    && a.DeletedAtUtc == null
                    && a.IncludeMerchandise
                    && (a.RouteSheetId ?? "") == rs,
                cancellationToken)
            .ConfigureAwait(false);
        if (ag is null)
            return null;

        if (!TradeAgreementService.TryResolveSingleAgreementCurrency(
                ag, payload, out var merchCur, out _))
            return RouteSheetMutationResult.RouteCurrencyMerchandiseMismatch;

        if (TradeAgreementService.ValidateRoutePayloadCurrency(payload, merchCur!) is not null)
            return RouteSheetMutationResult.RouteCurrencyMerchandiseMismatch;

        return null;
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

    public async Task<bool> AutoArchiveOnRouteCompletedAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string acceptedRouteStopId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsId = (routeSheetId ?? "").Trim();
        var sid = (acceptedRouteStopId ?? "").Trim();
        var actor = (actorUserId ?? "").Trim();
        if (tid.Length < 4 || rsId.Length < 1 || sid.Length < 1 || actor.Length < 2)
            return false;

        var row = await db.ChatRouteSheets.FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsId,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || row.DeletedAtUtc is not null)
            return false;

        var ordered = LogisticsUtils.OrderedStopIds(row.Payload);
        if (!IsLastOrderedStop(ordered, sid))
            return false;

        var deliveryRows = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsId)
            .Select(x => new { x.RouteStopId, x.State })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deliveryStates = deliveryRows
            .Select(r =>
                string.Equals((r.RouteStopId ?? "").Trim(), sid, StringComparison.Ordinal)
                    ? RouteStopDeliveryStates.EvidenceAccepted
                    : (r.State ?? "").Trim())
            .ToList();

        if (!ShouldAutoArchiveRouteSheet(ordered, sid, deliveryStates))
            return false;

        var completedAt = DateTimeOffset.UtcNow;

        var payload = row.Payload;
        payload.Estado = "entregada";
        payload.PublicadaPlataforma = false;
        payload.ActualizadoEn = completedAt.ToUnixTimeMilliseconds();
        RouteSheetPayloadPersistence.ApplyPayloadAndTouch(row, payload, completedAt);
        row.PublishedToPlatform = false;

        var emRow0 = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ThreadId == tid && e.RouteSheetId == rsId && e.RetractedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var emergentPubId = string.IsNullOrWhiteSpace(emRow0?.Id) ? null : emRow0!.Id.Trim();

        await RetractEmergentAsync(tid, rsId, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await routeSheetThreadNotifications.NotifyAfterRouteSheetAutoArchivedAsync(
                actor,
                tid,
                rsId,
                row.Payload.Titulo,
                emergentPubId,
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    public async Task<(RouteSheetPayload? Payload, RouteSheetMutationResult? Error)> DuplicateAsync(
        string userId,
        string threadId,
        string sourceRouteSheetId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsId = (sourceRouteSheetId ?? "").Trim();
        if (tid.Length < 4 || rsId.Length < 1)
            return (null, RouteSheetMutationResult.NotFoundOrForbidden);

        var sourceRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsId && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (sourceRow is null)
            return (null, RouteSheetMutationResult.NotFoundOrForbidden);

        var (newId, payload) = CloneForDuplicate(sourceRow.Payload, tid);
        var upsert = await UpsertInternalAsync(
            userId,
            tid,
            newId,
            payload,
            bypassUnpaidAgreementLimit: true,
            cancellationToken).ConfigureAwait(false);
        if (upsert != RouteSheetMutationResult.Ok)
            return (null, upsert);

        var created = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == newId && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        return created is null
            ? (null, RouteSheetMutationResult.NotFoundOrForbidden)
            : (created.Payload, null);
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
        var ackKey = RouteSheetUtils.ResolveCarrierKeyInEditAck(ack.ByCarrier, cid);
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
        var sheetTitle = RouteSheetUtils.TruncateTitle(sheetRow.Payload.Titulo);

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

            RouteSheetUtils.PersistSheetPayloadWithAck(sheetRow, ack, now, p => RouteSheetUtils.ClearTransportistaPhonesForSubs(p, subs));
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
            RouteSheetUtils.PersistSheetPayloadWithAck(sheetRow, ack, now);
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

        var title = RouteSheetUtils.TruncateTitle(sheetRow.Payload.Titulo);
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
            var inviteDigits = AuthUtils.DigitsOnly(inv.Phone);
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

            var onSheetDigits = AuthUtils.DigitsOnly(parada.TelefonoTransportista);
            var inviteMatchesSheetPhone =
                string.Equals(onSheetDigits, inviteDigits, StringComparison.Ordinal);
            if (!inviteMatchesSheetPhone)
            {
                // Tras <see cref="VibeTrade.Backend.Features.RouteTramoSubscriptions.RouteTramoSubscriptionService.PreselExecuteRejectAsync"/> el teléfono se borra del tramo;
                // el vendedor debe poder reenviar presel al mismo contacto (lista stopId + teléfono).
                var canReinviteAfterDecline = subsForSheet.Exists(s =>
                    string.Equals((s.StopId ?? "").Trim(), stopId, StringComparison.Ordinal)
                    && ChatThreadAccess.UserIdsMatchLoose(uid, s.CarrierUserId)
                    && RouteSheetUtils.PreselInviteEligibleAfterSheetPhoneCleared(s.Status));
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
                .Where(sid => !RouteSheetUtils.ShouldSkipPreselectedNotifyForSingleStop(
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
            RouteSheetUtils.ApplyStopContentFingerprintsAfterPreselectedNotify(
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

    private static (string NewRouteSheetId, RouteSheetPayload Payload) CloneForDuplicate(
        RouteSheetPayload source,
        string threadId)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var newRsId = "rs_" + Guid.NewGuid().ToString("N")[..16];
        var clone = RouteSheetUtils.ClonePayload(source);
        clone.Id = newRsId;
        clone.ThreadId = threadId;
        clone.Estado = "programada";
        clone.PublicadaPlataforma = false;
        clone.RouteSheetEditAck = null;
        clone.EditadaEnFormulario = null;
        clone.CreadoEn = nowMs;
        clone.ActualizadoEn = nowMs;

        var baseTitulo = (clone.Titulo ?? "").Trim();
        clone.Titulo = baseTitulo.Length > 0 ? $"{baseTitulo} (copia)" : "Hoja de ruta (copia)";

        clone.Paradas ??= new List<RouteStopPayload>();
        foreach (var p in clone.Paradas)
        {
            p.Id = "stop_" + Guid.NewGuid().ToString("N")[..12];
            p.Completada = null;
            p.TelefonoTransportista = null;
        }

        return (newRsId, clone);
    }
}
