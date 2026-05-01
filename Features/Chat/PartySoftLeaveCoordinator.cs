using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stripe;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Trust;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Penalización de confianza a la tienda al salir con pagos retenidos reembolsados (más agresiva que la salida normal).</summary>
internal static class PartySoftLeaveTrust
{
    public const int SellerExitWithHeldServiceRefundsPenalty = 15;
}

public sealed class PartySoftLeaveCoordinator(
    AppDbContext db,
    ITrustScoreLedgerService trustLedger) : IPartySoftLeaveCoordinator
{
    public async Task<PartySoftLeavePaymentPrep> ProcessPaymentRulesAsync(
        ChatThreadRow thread,
        bool isBuyer,
        bool isSeller,
        CancellationToken cancellationToken = default)
    {
        var tid = (thread.Id ?? "").Trim();
        if (tid.Length < 4)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        var hasHeld = await db.AgreementServicePayments.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == tid
                    && x.Status == AgreementServicePaymentStatuses.Held,
                cancellationToken)
            .ConfigureAwait(false);

        if (!hasHeld)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        if (isBuyer)
            return new PartySoftLeavePaymentPrep(false, "held_payments_buyer", false, false, null);

        if (!isSeller)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        if (!await AllAcceptedAgreementsAreServiceOnlyAsync(tid, cancellationToken).ConfigureAwait(false))
            return new PartySoftLeavePaymentPrep(false, "held_payments_seller_merchandise", false, false, null);

        if (await HasHeldServiceWithSubmittedEvidenceAwaitingBuyerAsync(tid, cancellationToken)
                .ConfigureAwait(false))
            return new PartySoftLeavePaymentPrep(false, "service_evidence_pending", false, false, null);

        return await RefundHeldServicePaymentsForSellerServiceExitAsync(
                tid,
                (thread.StoreId ?? "").Trim(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> AllAcceptedAgreementsAreServiceOnlyAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        var agrs = await db.TradeAgreements.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.Status == "accepted"
                && x.DeletedAtUtc == null)
            .Select(x => new { x.IncludeMerchandise, x.IncludeService })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (agrs.Count == 0)
            return false;

        return agrs.TrueForAll(a => !a.IncludeMerchandise && a.IncludeService);
    }

    /// <summary>
    /// Evidencia ya enviada al comprador (<c>submitted</c>) sin decisión: no permitir abandono con reembolso.
    /// Si está <c>rejected</c>, el comprador ya respondió — se permite el flujo de reembolso de pagos aún <c>held</c>.
    /// </summary>
    private async Task<bool> HasHeldServiceWithSubmittedEvidenceAwaitingBuyerAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        return await (
                from e in db.ServiceEvidences.AsNoTracking()
                join sp in db.AgreementServicePayments.AsNoTracking()
                    on e.AgreementServicePaymentId equals sp.Id
                where sp.ThreadId == threadId
                      && sp.Status == AgreementServicePaymentStatuses.Held
                      && e.Status == ServiceEvidenceStatuses.Submitted
                select e.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PartySoftLeavePaymentPrep> RefundHeldServicePaymentsForSellerServiceExitAsync(
        string threadId,
        string storeId,
        CancellationToken cancellationToken)
    {
        var heldList = await db.AgreementServicePayments
            .Where(x =>
                x.ThreadId == threadId
                && x.Status == AgreementServicePaymentStatuses.Held)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (heldList.Count == 0)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        var cpIds = heldList
            .Select(x => x.AgreementCurrencyPaymentId?.Trim())
            .Where(x => x is { Length: >= 4 })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cpIds.Count == 0)
            return new PartySoftLeavePaymentPrep(false, "stripe_refund_failed", false, false, null);

        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        var skipStripe = PaymentStripeEnv.SkipStripePaymentIntentCreate();
        if (!skipStripe && string.IsNullOrWhiteSpace(serverKey))
            return new PartySoftLeavePaymentPrep(false, "stripe_refund_failed", false, false, null);

        if (!skipStripe)
            StripeConfiguration.ApiKey = serverKey;

        var refundSvc = new RefundService();

        foreach (var cpId in cpIds)
        {
            var cp = await db.AgreementCurrencyPayments
                .FirstOrDefaultAsync(x => x.Id == cpId, cancellationToken)
                .ConfigureAwait(false);
            if (cp is null)
                return new PartySoftLeavePaymentPrep(false, "stripe_refund_failed", false, false, null);
            if (!string.Equals(cp.Status, AgreementPaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
                continue;

            var heldForCp = heldList
                .Where(x =>
                    string.Equals(x.AgreementCurrencyPaymentId?.Trim(), cpId, StringComparison.Ordinal))
                .ToList();
            var refundMinorTotal = heldForCp.Sum(x => x.AmountMinor);
            if (refundMinorTotal <= 0)
                continue;

            var piId = (cp.StripePaymentIntentId ?? "").Trim();
            if (piId.Length < 8)
                return new PartySoftLeavePaymentPrep(false, "stripe_refund_failed", false, false, null);

            if (!skipStripe && !piId.StartsWith("skipped_", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var refundOpts = new RefundCreateOptions { PaymentIntent = piId };
                    // Misma moneda PI: solo líneas aún held; las liberadas no se reembolsan (reembolso parcial Stripe).
                    if (refundMinorTotal < cp.TotalAmountMinor)
                        refundOpts.Amount = refundMinorTotal;

                    await refundSvc.CreateAsync(
                            refundOpts,
                            requestOptions: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (StripeException)
                {
                    return new PartySoftLeavePaymentPrep(false, "stripe_refund_failed", false, false, null);
                }
            }

            if (refundMinorTotal >= cp.TotalAmountMinor)
                cp.Status = AgreementPaymentStatuses.Refunded;

            foreach (var sp in heldForCp)
                sp.Status = AgreementServicePaymentStatuses.Refunded;
        }

        var refundNotice = await BuildSellerExitRefundNoticeAsync(heldList, cancellationToken)
            .ConfigureAwait(false);

        await ApplyAggressiveStorePenaltyAsync(storeId, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PartySoftLeavePaymentPrep(true, null, true, true, refundNotice);
    }

    private async Task<string> BuildSellerExitRefundNoticeAsync(
        IReadOnlyList<AgreementServicePaymentRow> heldRows,
        CancellationToken cancellationToken)
    {
        var ids = heldRows
            .Select(x => (x.TradeAgreementId ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var titleById = ids.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.TradeAgreements.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(
                    a => a.Id,
                    a => (a.Title ?? "").Trim(),
                    StringComparer.Ordinal,
                    cancellationToken)
                .ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine(
            "Los pagos retenidos por servicios en este chat fueron reembolsados al comprador por la salida del vendedor (acuerdos solo servicios).");
        sb.AppendLine();

        foreach (var sp in heldRows
                     .OrderBy(x => x.TradeAgreementId, StringComparer.Ordinal)
                     .ThenBy(x => x.EntryMonth)
                     .ThenBy(x => x.EntryDay))
        {
            var aid = (sp.TradeAgreementId ?? "").Trim();
            var rawTitle = titleById.TryGetValue(aid, out var tt) && tt.Length > 0 ? tt : aid;
            var title = rawTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.Length == 0)
                title = aid;

            sb.AppendLine(
                $"• «{title}» · mes {sp.EntryMonth} día {sp.EntryDay} · {FormatServicePaymentAmountForNotice(sp)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatServicePaymentAmountForNotice(AgreementServicePaymentRow sp)
    {
        var curLower = PaymentCheckoutComputation.NormalizeCurrency(sp.Currency ?? "usd");
        if (curLower.Length == 0)
            curLower = "usd";

        var curUp = curLower.ToUpperInvariant();
        var pow = PaymentCheckoutComputation.StripeMinorDecimals(curLower);
        var major = pow == 0 ? sp.AmountMinor : sp.AmountMinor / 100m;
        var culture = CultureInfo.GetCultureInfo("es-ES");
        var num = pow == 0 ? major.ToString("N0", culture) : major.ToString("N2", culture);
        return $"{num} {curUp}";
    }

    private async Task ApplyAggressiveStorePenaltyAsync(string storeId, CancellationToken cancellationToken)
    {
        var sid = storeId.Trim();
        if (sid.Length < 2)
            return;

        var storeRow = await db.Stores
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            .ConfigureAwait(false);
        if (storeRow is null)
            return;

        var prev = storeRow.TrustScore;
        storeRow.TrustScore = Math.Max(-10_000, prev - PartySoftLeaveTrust.SellerExitWithHeldServiceRefundsPenalty);
        trustLedger.StageEntry(
            TrustLedgerSubjects.Store,
            sid,
            storeRow.TrustScore - prev,
            storeRow.TrustScore,
            "Salida del vendedor del chat con pagos retenidos reembolsados al comprador (acuerdos solo servicios).");
    }
}
