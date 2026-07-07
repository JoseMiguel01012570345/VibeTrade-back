using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Trust.TrustMediator.ApplyCompletionBonus;

public sealed class ApplyCompletionBonusHandler(
    AppDbContext db,
    ITrustScoreLedgerService trustLedger,
    IChatThreadSystemMessageService threadSystemMessages) : IRequestHandler<ApplyCompletionBonusCommand>
{
    public async Task Handle(ApplyCompletionBonusCommand request, CancellationToken cancellationToken)
    {
        var tid = (request.ThreadId ?? "").Trim();
        var aid = (request.AgreementId ?? "").Trim();
        if (tid.Length < 4 || aid.Length < 8)
            return;

        if (!await IsAgreementFullyCompleteAsync(tid, aid, cancellationToken).ConfigureAwait(false))
            return;

        var ag = await db.TradeAgreements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == aid && x.ThreadId == tid, cancellationToken)
            .ConfigureAwait(false);
        if (ag is null)
            return;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return;

        var storeId = (ag.IssuedByStoreId ?? "").Trim();
        var buyerId = (thread.BuyerUserId ?? "").Trim();
        if (storeId.Length < 2 || buyerId.Length < 2)
            return;

        if (await CompletionBonusAlreadyAppliedAsync(storeId, aid, cancellationToken).ConfigureAwait(false))
            return;

        var storeRow = await db.Stores.FirstOrDefaultAsync(x => x.Id == storeId, cancellationToken)
            .ConfigureAwait(false);
        var buyerRow = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == buyerId, cancellationToken)
            .ConfigureAwait(false);
        if (storeRow is null || buyerRow is null)
            return;

        storeRow.TrustScore = Math.Max(-10_000, storeRow.TrustScore + TrustCompletionBonuses.StoreOnAgreementCompleted);
        trustLedger.StageEntry(
            TrustLedgerSubjects.Store,
            storeId,
            TrustCompletionBonuses.StoreOnAgreementCompleted,
            storeRow.TrustScore,
            $"{TrustCompletionBonuses.AgreementCompletionLedgerPrefix} ({aid})");

        buyerRow.TrustScore = Math.Max(-10_000, buyerRow.TrustScore + TrustCompletionBonuses.BuyerOnPurchaseCompleted);
        trustLedger.StageEntry(
            TrustLedgerSubjects.User,
            buyerId,
            TrustCompletionBonuses.BuyerOnPurchaseCompleted,
            buyerRow.TrustScore,
            TrustCompletionBonuses.BuyerPurchaseReason);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(
                tid,
                "Compra completada: confianza actualizada para comprador y tienda (demo).",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsAgreementFullyCompleteAsync(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var hasHeldService = await db.AgreementServicePayments.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == threadId
                    && x.TradeAgreementId == agreementId
                    && x.Status == AgreementServicePaymentStatuses.Held,
                cancellationToken)
            .ConfigureAwait(false);
        if (hasHeldService)
            return false;

        var ag = await db.TradeAgreements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == agreementId && x.ThreadId == threadId, cancellationToken)
            .ConfigureAwait(false);
        if (ag is null)
            return false;

        var rsid = (ag.RouteSheetId ?? "").Trim();
        if (rsid.Length > 0)
        {
            var row = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == threadId && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (row is null)
                return false;
            if (!string.Equals(
                    (row.Payload?.Estado ?? "").Trim(),
                    "entregada",
                    StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var activeDeliveries = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.TradeAgreementId == agreementId
                && x.State != RouteStopDeliveryStates.Unpaid
                && x.State != RouteStopDeliveryStates.Refunded
                && x.State != RouteStopDeliveryStates.EvidenceAccepted)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        return !activeDeliveries;
    }

    private async Task<bool> CompletionBonusAlreadyAppliedAsync(
        string storeId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var prefix = TrustCompletionBonuses.AgreementCompletionLedgerPrefix;
        return await db.TrustScoreLedgerRows.AsNoTracking()
            .AnyAsync(
                x => x.SubjectType == TrustLedgerSubjects.Store
                    && x.SubjectId == storeId
                    && x.Reason.StartsWith(prefix)
                    && x.Reason.Contains(agreementId),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
