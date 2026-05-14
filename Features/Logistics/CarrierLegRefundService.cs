using Microsoft.EntityFrameworkCore;
using Stripe;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierLegRefundService(IChatService chat, AppDbContext db) : ICarrierLegRefundService
{
    public async Task<(bool Ok, string? ErrorCode)> TryRefundEligibleLegAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken = default)
    {
        var uid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1)
            return (false, "not_found");

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return (false, "not_found");
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return (false, "not_found");

        var isBuyer = string.Equals(thread.BuyerUserId, uid, StringComparison.Ordinal);
        var isSeller = string.Equals(thread.SellerUserId, uid, StringComparison.Ordinal);
        if (!isBuyer && !isSeller)
            return (false, "forbidden");

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
            return (false, "not_found");

        if (delivery.RefundEligibleReason is null || delivery.RefundedAtUtc is not null)
            return (false, "not_eligible");

        var row = await (
                from rl in db.AgreementRouteLegPaids.AsNoTracking()
                join cp in db.AgreementCurrencyPayments.AsNoTracking()
                    on rl.AgreementCurrencyPaymentId equals cp.Id
                where cp.ThreadId == tid
                      && cp.TradeAgreementId == aid
                      && rl.RouteSheetId == rsid
                      && rl.RouteStopId == sid
                      && cp.Status == AgreementPaymentStatuses.Succeeded
                orderby cp.CreatedAtUtc descending
                select new { rl.AmountMinor, cp.Id, cp.StripePaymentIntentId, cp.TotalAmountMinor })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null || row.AmountMinor <= 0)
            return (false, "no_charge");

        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        var skipStripe = PaymentStripeEnv.SkipStripePaymentIntentCreate();
        if (!skipStripe && string.IsNullOrWhiteSpace(serverKey))
            return (false, "stripe_not_configured");

        if (!skipStripe)
            StripeConfiguration.ApiKey = serverKey;

        var piId = (row.StripePaymentIntentId ?? "").Trim();
        if (!skipStripe && piId.Length < 8)
            return (false, "stripe_missing_pi");

        if (!skipStripe && !piId.StartsWith("skipped_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var refundSvc = new RefundService();
                var refundOpts = new RefundCreateOptions { PaymentIntent = piId };
                if (row.AmountMinor < row.TotalAmountMinor)
                    refundOpts.Amount = row.AmountMinor;

                await refundSvc.CreateAsync(refundOpts, requestOptions: null, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (StripeException)
            {
                return (false, "stripe_refund_failed");
            }
        }

        var now = DateTimeOffset.UtcNow;
        delivery.RefundedAtUtc = now;
        delivery.State = RouteStopDeliveryStates.Refunded;
        delivery.UpdatedAtUtc = now;
        delivery.CurrentOwnerUserId = null;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await chat.PostAutomatedSystemThreadNoticeAsync(
                tid,
                $"Se inició un reembolso del tramo ({sid}) por motivo: {delivery.RefundEligibleReason}.",
                cancellationToken)
            .ConfigureAwait(false);

        return (true, null);
    }
}
