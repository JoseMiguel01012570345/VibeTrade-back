using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Payments.Interfaces;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Chat.Agreements;

public sealed class TradeAgreementService(
    AppDbContext db,
    IChatService chat,
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
            .Include(a => a.MerchandiseLines)
            .Include(a => a.MerchandiseMeta)
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
        return list.ConvertAll(a => TradeAgreementEntityToApiMapper.ToApiResponse(a, paidSet.Contains(a.Id)));
    }

    public async Task<(TradeAgreementApiResponse? Agreement, string? ErrorCode)> CreateAsync(
        string sellerUserId,
        string threadId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateDraft(draft))
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

        var id = NewAgrId();
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
            IncludeMerchandise = draft.IncludeMerchandise,
            IncludeService = draft.IncludeService,
        };

        TradeAgreementDraftToEntityMapper.ReplaceContentFromDraft(ag, draft);
        db.TradeAgreements.Add(ag);
        await db.SaveChangesAsync(cancellationToken);

        await chat.PostAgreementAnnouncementAsync(
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
        if (!ValidateDraft(draft))
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
        ag.MerchandiseLines.Clear();
        if (ag.MerchandiseMeta is not null)
        {
            db.TradeAgreementMerchandiseMetas.Remove(ag.MerchandiseMeta);
            ag.MerchandiseMeta = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        ag.Title = draft.Title.Trim();
        ag.IncludeMerchandise = draft.IncludeMerchandise;
        ag.IncludeService = draft.IncludeService;
        ag.Status = "pending_buyer";
        ag.RespondedAtUtc = null;
        ag.RespondedByUserId = null;
        ag.SellerEditBlockedUntilBuyerResponse = true;

        TradeAgreementDraftToEntityMapper.ReplaceContentFromDraft(ag, draft);
        await db.SaveChangesAsync(cancellationToken);

        var sys = wasAccepted
            ? $"El vendedor modificó el acuerdo «{ag.Title}», que estaba aceptado: vuelve a estar pendiente de aceptación del comprador, quien puede aceptarlo o rechazarlo sin abandonar el chat."
            : wasRejected
                ? $"El vendedor revisó el acuerdo «{ag.Title}» tras el rechazo; volvió a quedar pendiente de respuesta del comprador."
                : $"El vendedor actualizó el acuerdo «{ag.Title}» (sigue pendiente de respuesta del comprador).";

        await chat.PostSystemThreadNoticeAsync(sellerUserId, threadId, sys, cancellationToken);

        var updated = await GetTrackedResponseAsync(ag.Id, cancellationToken);
        return (updated, null);
    }

    public async Task<TradeAgreementApiResponse?> RespondAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatThreadAccess.UserCanSeeThread(buyerUserId, t))
            return null;
        if (t.IsSocialGroup)
            return null;
        if (buyerUserId != t.BuyerUserId)
            return null;

        var ag = await db.TradeAgreements.FirstOrDefaultAsync(
            x => x.Id == agreementId && x.ThreadId == threadId && x.DeletedAtUtc == null,
            cancellationToken);
        if (ag is null || ag.Status != "pending_buyer")
            return null;

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
                await chat.NotifySellerStoreTrustPenaltyAsync(
                    new SellerStoreTrustPenaltyNotificationArgs(
                        sellerUid,
                        threadId,
                        (t.OfferId ?? "").Trim(),
                        penaltyDeltaNotify,
                        penaltyBalanceAfter,
                        $"El comprador rechazó «{ag.Title}» después de una aceptación previa; la confianza de la tienda se ajustó en {demoPenaltyPts} pts (demo)."),
                    cancellationToken);
            }
        }

        var sys = accept
            ? $"Acuerdo «{ag.Title}» aceptado por ambas partes. El vendedor puede proponer una nueva versión editándolo; eso reabre la aceptación del comprador. Pueden coexistir otros contratos adicionales."
            : $"Acuerdo «{ag.Title}» rechazado por el comprador. El comprador permanece en el chat; pueden seguir negociando o el vendedor puede enviar una nueva versión.";

        await chat.PostSystemThreadNoticeAsync(buyerUserId, threadId, sys, cancellationToken);

        return await GetTrackedResponseAsync(ag.Id, cancellationToken);
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

        await chat.PostSystemThreadNoticeAsync(
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
        const string noMerchMsg =
            "Solo se puede vincular una hoja de ruta si el acuerdo incluye mercancía con al menos una línea con cantidad, precio unitario y moneda válidos.";

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
            .Include(a => a.MerchandiseLines)
            .Include(a => a.MerchandiseMeta)
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
        var incoming = (routeSheetId ?? "").Trim();
        var prevRs = (ag.RouteSheetId ?? "").Trim();
        if (paid)
        {
            if (string.Equals(incoming, prevRs, StringComparison.OrdinalIgnoreCase))
                return await OkResponseAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(incoming))
                return Fail(400, "No se puede desvincular la hoja de ruta: ya hay pagos registrados para este acuerdo.");
            return Fail(400, "No se puede cambiar la hoja de ruta vinculada: ya hay pagos registrados para este acuerdo.");
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
            if (!AgreementHasMerchandiseForRouteLink(ag))
                return Fail(400, noMerchMsg);

            var okRow = await db.ChatRouteSheets.AsNoTracking()
                .AnyAsync(
                    x => x.ThreadId == threadId
                         && x.RouteSheetId == incoming
                         && x.DeletedAtUtc == null,
                    cancellationToken);
            if (!okRow)
                return Fail(404, notFoundMsg);
            ag.RouteSheetId = incoming;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await OkResponseAsync().ConfigureAwait(false);
    }

    private async Task<TradeAgreementApiResponse?> GetTrackedResponseAsync(
        string agreementId,
        CancellationToken cancellationToken)
    {
        var ag = await LoadTrackedAgreementAsync(null, agreementId, cancellationToken);
        if (ag is null)
            return null;
        var paid = await HasSucceededPaymentAsync(ag.Id, cancellationToken);
        return TradeAgreementEntityToApiMapper.ToApiResponse(ag, paid);
    }

    private async Task<bool> HasSucceededPaymentAsync(string agreementId, CancellationToken cancellationToken)
    {
        return await db.AgreementCurrencyPayments.AsNoTracking()
            .AnyAsync(
                p => p.TradeAgreementId == agreementId && p.Status == AgreementPaymentStatuses.Succeeded,
                cancellationToken);
    }

    private async Task<TradeAgreementRow?> LoadTrackedAgreementAsync(
        string? threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var q = db.TradeAgreements.AsSplitQuery()
            .Include(a => a.MerchandiseLines)
            .Include(a => a.MerchandiseMeta)
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
        var wanted = NormalizeAgreementTitle(title);
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
            if (NormalizeAgreementTitle(existing) == wanted)
                return true;
        }

        return false;
    }

    private static string NormalizeAgreementTitle(string title) => (title ?? "").Trim().ToLowerInvariant();

    private static bool ValidateDraft(TradeAgreementDraftRequest d)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length > 512)
            return false;
        // XOR: debe elegir exactamente uno (mercancía o servicio).
        if (d.IncludeMerchandise == d.IncludeService)
            return false;
        return ValidateExtraFields(d);
    }

    private static bool ValidateExtraFields(TradeAgreementDraftRequest d)
    {
        var list = d.ExtraFields;
        if (list is null || list.Count == 0)
            return true;
        if (list.Count > 48)
            return false;

        foreach (var x in list)
        {
            if (IsSkippableEmptyExtraDraftApiRow(x))
                continue;

            var title = (x.Title ?? "").Trim();
            if (title.Length < 1 || title.Length > 256)
                return false;

            var rawKind = (x.ValueKind ?? "text").Trim().ToLowerInvariant();
            var kind = rawKind is "image" or "document" ? rawKind : "text";

            if (kind == "text")
            {
                var txt = (x.TextValue ?? "").Trim();
                if (txt.Length < 1 || txt.Length > 8000)
                    return false;
            }
            else
            {
                var url = (x.MediaUrl ?? "").Trim();
                if (url.Length < 24 || url.Length > 2048)
                    return false;
                if (!url.StartsWith("/api/v1/media/", StringComparison.Ordinal))
                    return false;
            }

            var fn = (x.FileName ?? "").Trim();
            if (fn.Length > 512)
                return false;
        }

        return true;
    }

    private static bool IsSkippableEmptyExtraDraftApiRow(TradeAgreementExtraFieldRequest x)
    {
        var title = (x.Title ?? "").Trim();
        if (title.Length > 0)
            return false;

        var rawKind = (x.ValueKind ?? "text").Trim().ToLowerInvariant();
        var kind = rawKind is "image" or "document" ? rawKind : "text";
        if (kind is "image" or "document")
            return string.IsNullOrWhiteSpace(x.MediaUrl);

        return string.IsNullOrWhiteSpace(x.TextValue);
    }

    private static string NewAgrId() => "agr_" + Guid.NewGuid().ToString("N")[..16];

    private static bool AgreementHasMerchandiseForRouteLink(TradeAgreementRow ag)
    {
        if (!ag.IncludeMerchandise)
            return false;
        foreach (var m in ag.MerchandiseLines.OrderBy(x => x.SortOrder))
        {
            if (!TryParsePositiveDecimal(m.Cantidad, out _))
                continue;
            if (!TryParsePositiveDecimal(m.ValorUnitario, out _))
                continue;
            var mon = PaymentCheckoutComputation.NormalizeCurrencyFirst(m.Moneda ?? ag.MerchandiseMeta?.Moneda);
            if (string.IsNullOrEmpty(mon))
                continue;
            return true;
        }

        return false;
    }

    private static bool TryParsePositiveDecimal(string? raw, out decimal value)
    {
        value = 0;
        var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal)
            .Replace('\u00a0', ' ');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return false;
        value = d;
        return d > 0;
    }

}
