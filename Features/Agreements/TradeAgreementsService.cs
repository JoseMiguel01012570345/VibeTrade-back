using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Agreements.Dtos;
using VibeTrade.Backend.Features.Agreements.Interfaces;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationDtos;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.Shared.Contracts.Events;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Agreements;

public sealed partial class TradeAgreementService(
    AppDbContext db,
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    IMediator mediator,
    INotificationService notifications,
    ITrustScoreLedgerService trustLedger) : ITradeAgreementService
{
    public async Task<IReadOnlyList<TradeAgreementApiResponse>> ListForThreadAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        if (tid.Length < 4)
            return Array.Empty<TradeAgreementApiResponse>();

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || !await chat.UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return Array.Empty<TradeAgreementApiResponse>();

        var list = await db.TradeAgreements.AsNoTracking()
            .AsSplitQuery()
            .Where(a => a.ThreadId == tid && a.DeletedAtUtc == null)
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleMonths)
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleDays)
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleOverrides)
            .Include(a => a.ServiceItems).ThenInclude(s => s.PaymentMonths)
            .Include(a => a.ServiceItems).ThenInclude(s => s.PaymentEntries)
            .Include(a => a.ServiceItems).ThenInclude(s => s.RiesgoItems)
            .Include(a => a.ServiceItems).ThenInclude(s => s.DependenciaItems)
            .Include(a => a.ServiceItems).ThenInclude(s => s.TerminacionCausas)
            .Include(a => a.ServiceItems).ThenInclude(s => s.MonedasAceptadas)
            .Include(a => a.ExtraFields)
            .OrderBy(a => a.IssuedAtUtc)
            .ToListAsync(cancellationToken);

        var ids = list.Select(a => a.Id).ToList();
        var paidIds = ids.Count == 0
            ? new List<string>()
            : await db.AgreementCurrencyPayments.AsNoTracking()
                .Where(p => ids.Contains(p.TradeAgreementId) && p.Status == AgreementPaymentStatuses.Succeeded)
                .Select(p => p.TradeAgreementId)
                .Distinct()
                .ToListAsync(cancellationToken);
        var paidSet = paidIds.ToHashSet(StringComparer.Ordinal);
        var routePaidSet = await LoadAgreementIdsWithSucceededRouteLegPaymentsAsync(ids, cancellationToken)
            .ConfigureAwait(false);
        return list.ConvertAll(a => TradeAgreementEntityToApiMapper.ToApiResponse(
            a,
            paidSet.Contains(a.Id),
            routePaidSet.Contains(a.Id)));
    }

    public async Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> CreateAsync(
        string sellerUserId,
        string threadId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default)
    {
        if (!AgreementUtils.ValidateDraft(draft))
            return (null, null);

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t))
            return (null, null);
        if (t.IsSocialGroup)
            return (null, null);
        if (sellerUserId != t.SellerUserId)
            return (null, null);

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == t.StoreId, cancellationToken);
        if (store is null)
            return (null, null);

        if (await ThreadHasAgreementWithSameTitleAsync(t.Id, draft.Title, excludeAgreementId: null, cancellationToken)
                .ConfigureAwait(false))
            return (null, TradeAgreementWriteErrors.DuplicateAgreementTitle);

        var id = AgreementUtils.NewAgreementRowId();
        var now = DateTimeOffset.UtcNow;
        var ag = new TradeAgreementRow
        {
            Id = id,
            ThreadId = t.Id,
            Title = draft.Title.Trim(),
            IssuedAtUtc = now,
            IssuedByStoreId = t.StoreId,
            IssuerLabel = string.IsNullOrWhiteSpace(store.Name) ? "Tienda" : store.Name.Trim(),
            Status = "pending_buyer",
            RespondedAtUtc = null,
            RespondedByUserId = null,
            SellerEditBlockedUntilBuyerResponse = false,
            IncludeService = draft.IncludeService,
        };

        TradeAgreementDraftToEntityMapper.ReplaceContentFromDraft(ag, draft);
        var currencyErr = await ValidateAgreementCurrencyAsync(ag, t.Id, cancellationToken)
            .ConfigureAwait(false);
        if (currencyErr is not null)
            return (null, TradeAgreementWriteErrors.SingleAgreementCurrency);

        db.TradeAgreements.Add(ag);
        await db.SaveChangesAsync(cancellationToken);

        await threadSystemMessages.PostAgreementAnnouncementAsync(
            new PostAgreementAnnouncementArgs(sellerUserId, threadId, id, ag.Title, "pending_buyer"),
            cancellationToken);

        var createdResp = await GetTrackedResponseAsync(id, cancellationToken);
        return (createdResp, null);
    }

    public async Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> UpdateAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default)
    {
        if (!AgreementUtils.ValidateDraft(draft))
            return (null, null);

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t))
            return (null, null);
        if (t.IsSocialGroup)
            return (null, null);
        if (sellerUserId != t.SellerUserId)
            return (null, null);

        var ag = await LoadTrackedAgreementAsync(threadId, agreementId, cancellationToken);
        if (ag is null)
            return (null, null);
        if (ag.IssuedByStoreId != t.StoreId)
            return (null, null);
        if (await HasSucceededPaymentAsync(ag.Id, cancellationToken))
            return (null, null);
        if (ag.SellerEditBlockedUntilBuyerResponse)
            return (null, null);
        if (ag.Status is not ("pending_buyer" or "rejected" or "accepted"))
            return (null, null);

        if (await ThreadHasAgreementWithSameTitleAsync(t.Id, draft.Title, excludeAgreementId: ag.Id,
                cancellationToken).ConfigureAwait(false))
            return (null, TradeAgreementWriteErrors.DuplicateAgreementTitle);

        var wasAccepted = ag.Status == "accepted";
        var wasRejected = ag.Status == "rejected";

        if (ag.ServiceItems.Count > 0)
            db.TradeAgreementServiceItems.RemoveRange(ag.ServiceItems);

        await db.SaveChangesAsync(cancellationToken);

        ag.Title = draft.Title.Trim();
        ag.IncludeService = draft.IncludeService;
        ag.Status = "pending_buyer";
        ag.RespondedAtUtc = null;
        ag.RespondedByUserId = null;
        ag.SellerEditBlockedUntilBuyerResponse = true;

        TradeAgreementDraftToEntityMapper.ReplaceContentFromDraft(ag, draft);
        var currencyErr = await ValidateAgreementCurrencyAsync(ag, t.Id, cancellationToken)
            .ConfigureAwait(false);
        if (currencyErr is not null)
            return (null, TradeAgreementWriteErrors.SingleAgreementCurrency);

        await db.SaveChangesAsync(cancellationToken);

        var sys = wasAccepted
            ? $"El vendedor modificó el acuerdo «{ag.Title}», que estaba aceptado: vuelve a estar pendiente de aceptación del comprador, quien puede aceptarlo o rechazarlo sin abandonar el chat."
            : wasRejected
                ? $"El vendedor revisó el acuerdo «{ag.Title}» tras el rechazo; volvió a quedar pendiente de respuesta del comprador."
                : $"El vendedor actualizó el acuerdo «{ag.Title}» (sigue pendiente de respuesta del comprador).";

        await threadSystemMessages.PostSystemThreadNoticeAsync(sellerUserId, threadId, sys, cancellationToken);

        var updated = await GetTrackedResponseAsync(ag.Id, cancellationToken);
        return (updated, null);
    }

    public async Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> RespondAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(buyerUserId, t))
            return (null, null);
        if (t.IsSocialGroup)
            return (null, null);
        if (buyerUserId != t.BuyerUserId)
            return (null, null);

        var ag = await LoadTrackedAgreementAsync(threadId, agreementId, cancellationToken);
        if (ag is null || ag.Status != "pending_buyer")
            return (null, null);

        if (accept)
        {
            var routePayload = await LoadRoutePayloadForAgreementAsync(ag, cancellationToken)
                .ConfigureAwait(false);
            if (!AgreementCheckoutCurrency.TryResolveSingleAgreementCurrency(ag, routePayload, out _, out _))
                return (null, TradeAgreementWriteErrors.SingleAgreementCurrency);
        }

        const int demoPenaltyPts = 3;
        var rejectAfterPriorAccept =
            !accept && ag.HadBuyerAcceptance;
        var penaltyStoreId = (ag.IssuedByStoreId ?? "").Trim();
        int penaltyDeltaNotify = 0;
        int penaltyBalanceAfter = 0;

        if (rejectAfterPriorAccept && penaltyStoreId.Length >= 2)
        {
            var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == penaltyStoreId, cancellationToken);
            if (storeRow is not null)
            {
                penaltyDeltaNotify = -demoPenaltyPts;
                storeRow.TrustScore = Math.Max(-10_000, storeRow.TrustScore + penaltyDeltaNotify);
                penaltyBalanceAfter = storeRow.TrustScore;
                trustLedger.StageEntry(
                    TrustLedgerSubjects.Store,
                    penaltyStoreId,
                    penaltyDeltaNotify,
                    storeRow.TrustScore,
                    "Rechazo del comprador con acuerdo previamente aceptado (demo)");
            }
        }

        var now = DateTimeOffset.UtcNow;
        ag.Status = accept ? "accepted" : "rejected";
        ag.RespondedAtUtc = now;
        ag.RespondedByUserId = buyerUserId;
        ag.SellerEditBlockedUntilBuyerResponse = false;

        if (accept)
            ag.HadBuyerAcceptance = true;

        await db.SaveChangesAsync(cancellationToken);

        if (rejectAfterPriorAccept && penaltyDeltaNotify != 0)
        {
            var sellerUid = (t.SellerUserId ?? "").Trim();
            if (sellerUid.Length >= 2)
            {
                var preview =
                    $"El comprador rechazó «{ag.Title}» después de una aceptación previa; la confianza de la tienda se ajustó en {demoPenaltyPts} pts (demo).";
                await notifications.NotifySellerStoreTrustPenaltyAsync(
                    new SellerStoreTrustPenaltyNotificationArgs(
                        sellerUid,
                        threadId,
                        (t.OfferId ?? "").Trim(),
                        penaltyDeltaNotify,
                        penaltyBalanceAfter,
                        preview),
                    cancellationToken);
            }
        }

        if (accept)
        {
            var buyerUid = (t.BuyerUserId ?? "").Trim();
            var sellerUid = (t.SellerUserId ?? "").Trim();
            if (buyerUid.Length >= 2 && sellerUid.Length >= 2)
            {
                await mediator.Publish(
                    new AgreementSignedEvent(ag.Id, buyerUid, sellerUid, threadId),
                    cancellationToken);
            }
        }

        var sys = accept
            ? $"Acuerdo «{ag.Title}» aceptado por ambas partes. El vendedor puede proponer una nueva versión editándolo; eso reabre la aceptación del comprador. Pueden coexistir otros contratos adicionales."
            : $"Acuerdo «{ag.Title}» rechazado por el comprador. El comprador permanece en el chat; pueden seguir negociando o el vendedor puede enviar una nueva versión.";

        await threadSystemMessages.PostSystemThreadNoticeAsync(buyerUserId, threadId, sys, cancellationToken);

        return (await GetTrackedResponseAsync(ag.Id, cancellationToken), null);
    }

    public async Task<bool> DeleteAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t))
            return false;
        if (sellerUserId != t.SellerUserId)
            return false;

        var ag = await db.TradeAgreements.FirstOrDefaultAsync(
            x => x.Id == agreementId && x.ThreadId == threadId,
            cancellationToken);
        if (ag is null
            || ag.DeletedAtUtc is not null
            || ag.Status == "accepted"
            || ag.IssuedByStoreId != t.StoreId)
            return false;
        if (await HasSucceededPaymentAsync(ag.Id, cancellationToken))
            return false;

        var title = ag.Title;
        ag.DeletedAtUtc = DateTimeOffset.UtcNow;
        ag.DeletedByUserId = sellerUserId;
        await db.SaveChangesAsync(cancellationToken);

        await threadSystemMessages.PostSystemThreadNoticeAsync(
            sellerUserId,
            threadId,
            $"Se eliminó el acuerdo «{title}» del hilo (no aplica a acuerdos ya aceptados).",
            cancellationToken);

        return true;
    }

    public async Task<TradeAgreementRouteSheetLinkOutcome> SetRouteSheetLinkAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string? routeSheetId,
        CancellationToken cancellationToken = default)
    {
        const string notFoundMsg = "No se pudo actualizar el vínculo con la hoja de ruta.";

        TradeAgreementRouteSheetLinkOutcome Fail(int code, string msg) =>
            new(null, code, msg);

        var t = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null
            || t.DeletedAtUtc is not null
            || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t)
            || sellerUserId != t.SellerUserId)
            return Fail(404, notFoundMsg);

        var ag = await db.TradeAgreements
            .FirstOrDefaultAsync(
                x => x.Id == agreementId
                     && x.ThreadId == threadId
                     && x.DeletedAtUtc == null
                     && x.IssuedByStoreId == t.StoreId,
                cancellationToken);
        if (ag is null)
            return Fail(404, notFoundMsg);

        async Task<TradeAgreementRouteSheetLinkOutcome> OkResponseAsync()
        {
            var r = await GetTrackedResponseAsync(ag.Id, cancellationToken)
                .ConfigureAwait(false);
            return r is null ? Fail(404, notFoundMsg) : new TradeAgreementRouteSheetLinkOutcome(r, null, null);
        }

        var paid = await HasSucceededPaymentAsync(ag.Id, cancellationToken);
        var routeTransportPaid = await HasSucceededRouteLegPaymentAsync(ag.Id, cancellationToken);
        var incoming = (routeSheetId ?? "").Trim();
        var prevRs = (ag.RouteSheetId ?? "").Trim();
        if (routeTransportPaid)
        {
            if (string.Equals(incoming, prevRs, StringComparison.OrdinalIgnoreCase))
                return await OkResponseAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(incoming))
                return Fail(400, "No se puede desvincular la hoja de ruta: ya hay pagos de transporte registrados para este acuerdo.");
            if (!string.IsNullOrEmpty(prevRs))
                return Fail(400, "No se puede cambiar la hoja de ruta vinculada: ya hay pagos de transporte registrados para este acuerdo.");
        }
        else if (paid
                 && string.Equals(incoming, prevRs, StringComparison.OrdinalIgnoreCase))
        {
            return await OkResponseAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(incoming))
        {
            if (string.IsNullOrWhiteSpace(ag.RouteSheetId))
                return await OkResponseAsync().ConfigureAwait(false);

            var prevId = ag.RouteSheetId!.Trim();
            var prevRow = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == threadId
                         && x.RouteSheetId == prevId
                         && x.DeletedAtUtc == null,
                    cancellationToken);
            if (prevRow?.PublishedToPlatform == true)
                return Fail(404, notFoundMsg);
            ag.RouteSheetId = null;
        }
        else
        {
            var okRow = await db.ChatRouteSheets.AsNoTracking()
                .AnyAsync(
                    x => x.ThreadId == threadId
                         && x.RouteSheetId == incoming
                         && x.DeletedAtUtc == null,
                    cancellationToken);
            if (!okRow)
                return Fail(404, notFoundMsg);

            var linkedToOther = await db.TradeAgreements.AsNoTracking()
                .AnyAsync(
                    x => x.ThreadId == threadId
                         && x.DeletedAtUtc == null
                         && x.Id != agreementId
                         && x.RouteSheetId != null
                         && x.RouteSheetId == incoming,
                    cancellationToken)
                .ConfigureAwait(false);
            if (linkedToOther)
                return Fail(
                    StatusCodes.Status409Conflict,
                    "Esta hoja de ruta ya está vinculada a otro acuerdo en este chat.");

            var routeRow = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == threadId
                         && x.RouteSheetId == incoming
                         && x.DeletedAtUtc == null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (routeRow is null)
                return Fail(404, notFoundMsg);

            var prevForRollback = ag.RouteSheetId;
            ag.RouteSheetId = incoming;
            if (!AgreementCheckoutCurrency.TryResolveSingleAgreementCurrency(ag, routeRow.Payload, out _, out var linkCurErr))
            {
                ag.RouteSheetId = prevForRollback;
                return Fail(
                    StatusCodes.Status400BadRequest,
                    linkCurErr ?? AgreementCheckoutCurrency.MultipleAgreementCurrenciesMessage);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await OkResponseAsync().ConfigureAwait(false);
    }

    public async Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> DuplicateAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken)
            .ConfigureAwait(false);
        if (t is null
            || t.DeletedAtUtc is not null
            || !ChatThreadAccess.UserCanSeeThread(sellerUserId, t)
            || sellerUserId != t.SellerUserId)
            return (null, null);

        var source = await LoadTrackedAgreementAsync(threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);
        if (source is null || source.DeletedAtUtc is not null)
            return (null, null);

        var draft = TradeAgreementApiToDraftMapper.ToDraftRequest(source);
        foreach (var svc in draft.Services)
            svc.Id = null;
        draft.Title = await BuildUniqueCopyTitleAsync(threadId, source.Title, cancellationToken)
            .ConfigureAwait(false);

        return await CreateAsync(sellerUserId, threadId, draft, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> BuildUniqueCopyTitleAsync(
        string threadId,
        string originalTitle,
        CancellationToken cancellationToken)
    {
        var trimmed = StripCopyTitleSuffix(originalTitle);
        if (trimmed.Length == 0)
            trimmed = "Acuerdo";

        var candidates = new List<string> { $"{trimmed} (copia)" };
        for (var n = 2; n <= 20; n++)
            candidates.Add($"{trimmed} (copia {n})");

        foreach (var candidate in candidates)
        {
            if (!await ThreadHasAgreementWithSameTitleAsync(threadId, candidate, null, cancellationToken)
                    .ConfigureAwait(false))
                return candidate;
        }

        return $"{trimmed} (copia {Guid.NewGuid():N[..6]})";
    }

    private static string StripCopyTitleSuffix(string originalTitle)
    {
        var trimmed = (originalTitle ?? "").Trim();
        if (trimmed.Length == 0)
            return "Acuerdo";

        var idx = trimmed.LastIndexOf(" (copia", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return trimmed;

        var suffix = trimmed[idx..];
        if (suffix.Equals(" (copia)", StringComparison.OrdinalIgnoreCase))
            return trimmed[..idx].Trim();

        if (suffix.StartsWith(" (copia ", StringComparison.OrdinalIgnoreCase)
            && suffix.EndsWith(')')
            && int.TryParse(suffix[" (copia ".Length..^1], out _))
            return trimmed[..idx].Trim();

        return trimmed;
    }

    private async Task<TradeAgreementApiResponse?> GetTrackedResponseAsync(
        string agreementId,
        CancellationToken cancellationToken)
    {
        var ag = await LoadTrackedAgreementAsync(null, agreementId, cancellationToken);
        if (ag is null)
            return null;
        var paid = await HasSucceededPaymentAsync(ag.Id, cancellationToken);
        var routePaid = await HasSucceededRouteLegPaymentAsync(ag.Id, cancellationToken);
        return TradeAgreementEntityToApiMapper.ToApiResponse(ag, paid, routePaid);
    }

    private async Task<bool> HasSucceededPaymentAsync(string agreementId, CancellationToken cancellationToken)
    {
        return await db.AgreementCurrencyPayments.AsNoTracking()
            .AnyAsync(
                p => p.TradeAgreementId == agreementId && p.Status == AgreementPaymentStatuses.Succeeded,
                cancellationToken);
    }

    private async Task<bool> HasSucceededRouteLegPaymentAsync(
        string agreementId,
        CancellationToken cancellationToken)
    {
        var aid = agreementId.Trim();
        if (aid.Length < 2)
            return false;
        return await (
                from rl in db.AgreementRouteLegPaids.AsNoTracking()
                join cp in db.AgreementCurrencyPayments.AsNoTracking()
                    on rl.AgreementCurrencyPaymentId equals cp.Id
                where cp.TradeAgreementId == aid && cp.Status == AgreementPaymentStatuses.Succeeded
                select rl.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HashSet<string>> LoadAgreementIdsWithSucceededRouteLegPaymentsAsync(
        IReadOnlyList<string> agreementIds,
        CancellationToken cancellationToken)
    {
        if (agreementIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);
        var ids = await (
                from rl in db.AgreementRouteLegPaids.AsNoTracking()
                join cp in db.AgreementCurrencyPayments.AsNoTracking()
                    on rl.AgreementCurrencyPaymentId equals cp.Id
                where agreementIds.Contains(cp.TradeAgreementId)
                      && cp.Status == AgreementPaymentStatuses.Succeeded
                select cp.TradeAgreementId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<TradeAgreementRow?> LoadTrackedAgreementAsync(
        string? threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var q = db.TradeAgreements.AsSplitQuery()
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleMonths)
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleDays)
            .Include(a => a.ServiceItems).ThenInclude(s => s.ScheduleOverrides)
            .Include(a => a.ServiceItems).ThenInclude(s => s.PaymentMonths)
            .Include(a => a.ServiceItems).ThenInclude(s => s.PaymentEntries)
            .Include(a => a.ServiceItems).ThenInclude(s => s.RiesgoItems)
            .Include(a => a.ServiceItems).ThenInclude(s => s.DependenciaItems)
            .Include(a => a.ServiceItems).ThenInclude(s => s.TerminacionCausas)
            .Include(a => a.ServiceItems).ThenInclude(s => s.MonedasAceptadas)
            .Include(a => a.ExtraFields)
            .Where(a => a.Id == agreementId && a.DeletedAtUtc == null);
        if (threadId is not null)
            q = q.Where(a => a.ThreadId == threadId);
        return await q.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Acuerdos no eliminados en el mismo hilo; título comparado tras trim y sin distinguir mayúsculas.</summary>
    private async Task<bool> ThreadHasAgreementWithSameTitleAsync(
        string threadId,
        string title,
        string? excludeAgreementId,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var wanted = AgreementUtils.NormalizeAgreementTitle(title);
        if (wanted.Length == 0)
            return false;

        var titles = await db.TradeAgreements.AsNoTracking()
            .Where(a => a.ThreadId == tid && a.DeletedAtUtc == null)
            .Where(a => excludeAgreementId == null || a.Id != excludeAgreementId)
            .Select(a => a.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var existing in titles)
        {
            if (AgreementUtils.NormalizeAgreementTitle(existing) == wanted)
                return true;
        }

        return false;
    }

    private async Task<RouteSheetPayload?> LoadRoutePayloadForAgreementAsync(
        TradeAgreementRow ag,
        CancellationToken cancellationToken)
    {
        var rsId = (ag.RouteSheetId ?? "").Trim();
        var tid = (ag.ThreadId ?? "").Trim();
        if (rsId.Length == 0 || tid.Length < 4)
            return null;

        var row = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsId && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        return row?.Payload;
    }

    private async Task<string?> ValidateAgreementCurrencyAsync(
        TradeAgreementRow ag,
        string threadId,
        CancellationToken cancellationToken)
    {
        var routePayload = await LoadRoutePayloadForAgreementAsync(ag, cancellationToken)
            .ConfigureAwait(false);
        if (!AgreementCheckoutCurrency.TryResolveSingleAgreementCurrency(ag, routePayload, out _, out var err))
            return err;

        return null;
    }

}
