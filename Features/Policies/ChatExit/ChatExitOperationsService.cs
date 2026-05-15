using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Policies;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <inheritdoc />
public sealed class ChatExitOperationsService(
    AppDbContext db,
    IPartySoftLeaveCoordinator partySoftLeave,
    INotificationService notifications,
    IBroadcastingService broadcasting,
    IChatThreadSystemMessageService threadSystemMessages,
    IRouteTramoSubscriptionService routeTramoSubscriptions) : IChatExitOperationsService
{
    /// <inheritdoc />
    public async Task<PartySoftLeaveResult> PartySoftLeaveAsync(
        PartySoftLeaveArgs args,
        CancellationToken cancellationToken = default)
    {
        var tid = (args.ThreadId ?? "").Trim();
        var uid = (args.UserId ?? "").Trim();
        var reasonTrim = (args.Reason ?? "").Trim();
        if (tid.Length < 4 || uid.Length < 2 || reasonTrim.Length < 1)
            return new PartySoftLeaveResult(false, "party_leave_invalid_request", false);

        var t = await db.ChatThreads.FirstOrDefaultAsync(x => x.Id == tid && x.DeletedAtUtc == null, cancellationToken);
        if (t is null)
            return new PartySoftLeaveResult(false, "party_leave_thread_not_found", false);

        var isBuyer = string.Equals(uid, t.BuyerUserId, StringComparison.Ordinal);
        var isSeller = string.Equals(uid, t.SellerUserId, StringComparison.Ordinal);
        if (!isBuyer && !isSeller)
            return new PartySoftLeaveResult(false, "not_eligible_party", false);

        if (isBuyer && t.BuyerExpelledAtUtc is not null)
            return new PartySoftLeaveResult(true, null, false);
        if (isSeller && t.SellerExpelledAtUtc is not null)
            return new PartySoftLeaveResult(true, null, false);

        if (!await HasAcceptedNonDeletedTradeAgreementOnThreadAsync(tid, cancellationToken))
            return new PartySoftLeaveResult(false, "party_leave_no_accepted_agreement", false);

        var paymentPrep = await partySoftLeave.ProcessPaymentRulesAsync(t, isBuyer, isSeller, cancellationToken)
            .ConfigureAwait(false);
        if (!paymentPrep.AllowProceed)
            return new PartySoftLeaveResult(false, paymentPrep.ErrorCode, false);

        if (!await notifications.TryPostPartySoftLeaveSystemThreadNoticeAsync(
                threadSystemMessages, uid, tid, isSeller, reasonTrim, cancellationToken))
            return new PartySoftLeaveResult(false, "party_leave_notice_failed", false);

        var now = DateTimeOffset.UtcNow;
        ApplyPartyExpulsionToThread(t, uid, isBuyer, reasonTrim, now);
        await db.SaveChangesAsync(cancellationToken);
        if (paymentPrep.RefundedBuyerHeldPayments)
        {
            const string defaultRefundNotice =
                "Los pagos retenidos en este chat fueron reembolsados al comprador por la salida del vendedor (acuerdos solo servicios o solo mercadería).";
            var refundBody = string.IsNullOrWhiteSpace(paymentPrep.RefundNoticeText)
                ? defaultRefundNotice
                : paymentPrep.RefundNoticeText.Trim();
            await threadSystemMessages.PostAutomatedSystemThreadNoticeAsync(tid, refundBody, cancellationToken)
                .ConfigureAwait(false);
        }

        await notifications.NotifyCounterpartyOfPartySoftLeaveAsync(t, uid, isSeller, reasonTrim, cancellationToken);
        await broadcasting.BroadcastPeerPartyExitedChatAsync(
            t, tid, uid, t.PartyExitedReason, t.PartyExitedAtUtc, isSeller, cancellationToken);
        return new PartySoftLeaveResult(
            true,
            null,
            paymentPrep.SkipClientTrustPenalty,
            paymentPrep.OtherMemberCount,
            paymentPrep.OtherMemberPenaltyApplied,
            paymentPrep.TrustScoreAfterMemberPenalty);
    }

    private async Task<bool> HasAcceptedNonDeletedTradeAgreementOnThreadAsync(
        string threadId,
        CancellationToken cancellationToken) =>
        await db.TradeAgreements.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == threadId
                    && x.Status == "accepted"
                    && x.DeletedAtUtc == null,
                cancellationToken);

    private static void ApplyPartyExpulsionToThread(
        ChatThreadRow t,
        string uid,
        bool isBuyer,
        string reasonTrim,
        DateTimeOffset now)
    {
        if (isBuyer)
            t.BuyerExpelledAtUtc = now;
        else
            t.SellerExpelledAtUtc = now;
        t.PartyExitedUserId = uid;
        t.PartyExitedReason = reasonTrim.Length > 2000 ? reasonTrim[..2000] : reasonTrim;
        t.PartyExitedAtUtc = now;
    }

    /// <inheritdoc />
    public Task<CarrierWithdrawFromThreadResult?> CarrierWithdrawAsync(
        string userId,
        string threadId,
        CancellationToken cancellationToken = default) =>
        routeTramoSubscriptions.WithdrawCarrierFromThreadAsync(userId, threadId, cancellationToken);
}
