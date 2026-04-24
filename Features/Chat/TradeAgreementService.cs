using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Chat;

public sealed class TradeAgreementService(AppDbContext db, IChatService chat) : ITradeAgreementService
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
            .OrderBy(a => a.IssuedAtUtc)
            .ToListAsync(cancellationToken);

        return list.ConvertAll(TradeAgreementEntityToApiMapper.ToApiResponse);
    }

    public async Task<TradeAgreementApiResponse?> CreateAsync(
        string sellerUserId,
        string threadId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateDraft(draft))
            return null;

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(sellerUserId, t))
            return null;
        if (sellerUserId != t.SellerUserId)
            return null;

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == t.StoreId, cancellationToken);
        if (store is null)
            return null;

        var id = NewAgrId();
        var now = DateTimeOffset.UtcNow;
        var ag = new TradeAgreementRow
        {
            Id = id,
            ThreadId = threadId,
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
            sellerUserId,
            threadId,
            id,
            ag.Title,
            "pending_buyer",
            cancellationToken);

        return await GetTrackedResponseAsync(id, cancellationToken);
    }

    public async Task<TradeAgreementApiResponse?> UpdateAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        TradeAgreementDraftRequest draft,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateDraft(draft))
            return null;

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(sellerUserId, t))
            return null;
        if (sellerUserId != t.SellerUserId)
            return null;

        var ag = await LoadTrackedAgreementAsync(threadId, agreementId, cancellationToken);
        if (ag is null)
            return null;
        if (ag.IssuedByStoreId != t.StoreId)
            return null;
        if (ag.SellerEditBlockedUntilBuyerResponse)
            return null;
        if (ag.Status is not ("pending_buyer" or "rejected" or "accepted"))
            return null;

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

        return await GetTrackedResponseAsync(ag.Id, cancellationToken);
    }

    public async Task<TradeAgreementApiResponse?> RespondAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(buyerUserId, t))
            return null;
        if (buyerUserId != t.BuyerUserId)
            return null;

        var ag = await db.TradeAgreements.FirstOrDefaultAsync(
            x => x.Id == agreementId && x.ThreadId == threadId && x.DeletedAtUtc == null,
            cancellationToken);
        if (ag is null || ag.Status != "pending_buyer")
            return null;

        var now = DateTimeOffset.UtcNow;
        ag.Status = accept ? "accepted" : "rejected";
        ag.RespondedAtUtc = now;
        ag.RespondedByUserId = buyerUserId;
        ag.SellerEditBlockedUntilBuyerResponse = false;
        await db.SaveChangesAsync(cancellationToken);

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
        if (t is null || t.DeletedAtUtc is not null || !ChatService.UserCanSeeThread(sellerUserId, t))
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

    public async Task<TradeAgreementApiResponse?> SetRouteSheetLinkAsync(
        string sellerUserId,
        string threadId,
        string agreementId,
        string? routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (t is null
            || t.DeletedAtUtc is not null
            || !ChatService.UserCanSeeThread(sellerUserId, t)
            || sellerUserId != t.SellerUserId)
            return null;

        var ag = await db.TradeAgreements
            .FirstOrDefaultAsync(
                x => x.Id == agreementId
                     && x.ThreadId == threadId
                     && x.DeletedAtUtc == null
                     && x.IssuedByStoreId == t.StoreId,
                cancellationToken);
        if (ag is null)
            return null;

        var incoming = (routeSheetId ?? "").Trim();
        if (string.IsNullOrEmpty(incoming))
        {
            if (string.IsNullOrWhiteSpace(ag.RouteSheetId))
                return await GetTrackedResponseAsync(ag.Id, cancellationToken);

            var prevId = ag.RouteSheetId!.Trim();
            var prevRow = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == threadId
                         && x.RouteSheetId == prevId
                         && x.DeletedAtUtc == null,
                    cancellationToken);
            if (prevRow?.PublishedToPlatform == true)
                return null;
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
                return null;
            ag.RouteSheetId = incoming;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetTrackedResponseAsync(ag.Id, cancellationToken);
    }

    private async Task<TradeAgreementApiResponse?> GetTrackedResponseAsync(
        string agreementId,
        CancellationToken cancellationToken)
    {
        var ag = await LoadTrackedAgreementAsync(null, agreementId, cancellationToken);
        return ag is null ? null : TradeAgreementEntityToApiMapper.ToApiResponse(ag);
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
            .Where(a => a.Id == agreementId && a.DeletedAtUtc == null);
        if (threadId is not null)
            q = q.Where(a => a.ThreadId == threadId);
        return await q.FirstOrDefaultAsync(cancellationToken);
    }

    private static bool ValidateDraft(TradeAgreementDraftRequest d)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length > 512)
            return false;
        if (!d.IncludeMerchandise && !d.IncludeService)
            return false;
        return true;
    }

    private static string NewAgrId() => "agr_" + Guid.NewGuid().ToString("N")[..16];
}
