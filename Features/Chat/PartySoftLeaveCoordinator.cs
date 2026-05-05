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
    public const int SellerExitWithHeldRefundsPenalty = 15;

    /// <summary>Penalización por cada otro integrante activo del chat (contraparte + transportistas no retirados).</summary>
    public const int PerOtherMemberPenalty = 3;
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

        var routeBlock = await EvaluateRouteDeliveryLeaveGateAsync(tid, isBuyer, isSeller, cancellationToken)
            .ConfigureAwait(false);
        if (routeBlock.AllowProceed == false)
            return routeBlock;

        var hasHeldService = await db.AgreementServicePayments.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == tid
                    && x.Status == AgreementServicePaymentStatuses.Held,
                cancellationToken)
            .ConfigureAwait(false);
        var hasHeldMerch = await db.AgreementMerchandiseLinePaids.AsNoTracking()
            .AnyAsync(
                x => x.ThreadId == tid
                    && x.Status == AgreementMerchandiseLinePaidStatuses.Held,
                cancellationToken)
            .ConfigureAwait(false);
        var hasHeld = hasHeldService || hasHeldMerch;

        if (!hasHeld)
        {
            var penalty = await ApplyOtherMemberSoftLeavePenaltyIfNeededAsync(
                    thread,
                    isBuyer,
                    isSeller,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PartySoftLeavePaymentPrep(
                true,
                null,
                true,
                false,
                null,
                penalty.MemberCount,
                penalty.Applied,
                penalty.TrustScoreAfter);
        }

        if (isBuyer)
            return new PartySoftLeavePaymentPrep(false, "held_payments_buyer", false, false, null);

        if (!isSeller)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        if (await AnyAcceptedAgreementMixesServiceAndMerchandiseAsync(tid, cancellationToken).ConfigureAwait(false))
            return new PartySoftLeavePaymentPrep(false, "held_payments_seller_mixed", false, false, null);

        if (await HasHeldPaymentWithEvidenceBlockingExitAsync(tid, cancellationToken).ConfigureAwait(false))
            return new PartySoftLeavePaymentPrep(false, "evidence_pending", false, false, null);

        return await RefundHeldPaymentsForSellerExitAsync(
                tid,
                (thread.StoreId ?? "").Trim(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record OtherMemberPenaltyResult(int MemberCount, bool Applied, int? TrustScoreAfter);

    private async Task<OtherMemberPenaltyResult> ApplyOtherMemberSoftLeavePenaltyIfNeededAsync(
        ChatThreadRow thread,
        bool isBuyer,
        bool isSeller,
        CancellationToken cancellationToken)
    {
        var tid = (thread.Id ?? "").Trim();
        var otherPartyAlreadyExited = isSeller
            ? thread.BuyerExpelledAtUtc is not null
            : thread.SellerExpelledAtUtc is not null;
        var otherPartyCount = otherPartyAlreadyExited ? 0 : 1;

        var carrierCount = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.Status != "withdrawn")
            .Select(x => x.CarrierUserId)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var memberCount = otherPartyCount + carrierCount;
        if (memberCount <= 0)
            return new OtherMemberPenaltyResult(0, false, null);

        var delta = -PartySoftLeaveTrust.PerOtherMemberPenalty * memberCount;
        var reason =
            $"Salida del {(isSeller ? "vendedor" : "comprador")} con {memberCount} integrante(s) en el chat (demo).";

        if (isSeller)
        {
            var sid = (thread.StoreId ?? "").Trim();
            if (sid.Length < 2)
                return new OtherMemberPenaltyResult(memberCount, false, null);
            var storeRow = await db.Stores
                .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
                .ConfigureAwait(false);
            if (storeRow is null)
                return new OtherMemberPenaltyResult(memberCount, false, null);
            var prev = storeRow.TrustScore;
            storeRow.TrustScore = Math.Max(-10_000, prev + delta);
            trustLedger.StageEntry(
                TrustLedgerSubjects.Store,
                sid,
                storeRow.TrustScore - prev,
                storeRow.TrustScore,
                reason);
            return new OtherMemberPenaltyResult(memberCount, true, storeRow.TrustScore);
        }

        var buyerId = (thread.BuyerUserId ?? "").Trim();
        if (buyerId.Length < 2)
            return new OtherMemberPenaltyResult(memberCount, false, null);
        var acc = await db.UserAccounts
            .FirstOrDefaultAsync(x => x.Id == buyerId, cancellationToken)
            .ConfigureAwait(false);
        if (acc is null)
            return new OtherMemberPenaltyResult(memberCount, false, null);
        var prevU = acc.TrustScore;
        acc.TrustScore = Math.Max(-10_000, prevU + delta);
        trustLedger.StageEntry(
            TrustLedgerSubjects.User,
            buyerId,
            acc.TrustScore - prevU,
            acc.TrustScore,
            reason);
        return new OtherMemberPenaltyResult(memberCount, true, acc.TrustScore);
    }

    private async Task<PartySoftLeavePaymentPrep> EvaluateRouteDeliveryLeaveGateAsync(
        string threadId,
        bool isBuyer,
        bool isSeller,
        CancellationToken cancellationToken)
    {
        var active = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.RefundedAtUtc == null
                && x.RefundEligibleReason == null
                && x.State != RouteStopDeliveryStates.Unpaid
                && x.State != RouteStopDeliveryStates.RefundedExpired
                && x.State != RouteStopDeliveryStates.RefundedCarrierExit
                && x.State != RouteStopDeliveryStates.EvidenceAccepted)
            .Select(x => new { x.TradeAgreementId, x.State })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (active.Count == 0)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        var agreementIds = active.Select(x => x.TradeAgreementId.Trim()).Where(x => x.Length >= 8).Distinct()
            .ToList();
        var acceptedAgreementIds = agreementIds.Count == 0
            ? []
            : await db.TradeAgreements.AsNoTracking()
                .Where(a => agreementIds.Contains(a.Id) && a.Status == "accepted" && a.DeletedAtUtc == null)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        if (acceptedAgreementIds.Count == 0)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        active = active.Where(x => acceptedAgreementIds.Contains(x.TradeAgreementId.Trim())).ToList();
        if (active.Count == 0)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        if (isBuyer && active.Count > 0)
            return new PartySoftLeavePaymentPrep(false, "route_delivery_active_buyer", false, false, null);

        if (isSeller && active.Count > 0)
            return new PartySoftLeavePaymentPrep(false, "route_delivery_active_seller", false, false, null);

        return new PartySoftLeavePaymentPrep(true, null, false, false, null);
    }

    private async Task<bool> AnyAcceptedAgreementMixesServiceAndMerchandiseAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        return await db.TradeAgreements.AsNoTracking()
            .AnyAsync(
                x =>
                    x.ThreadId == threadId
                    && x.Status == "accepted"
                    && x.DeletedAtUtc == null
                    && x.IncludeMerchandise
                    && x.IncludeService,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Evidencia enviada o rechazada con pago aún retenido: no permitir abandono con reembolso hasta decisión/liberación.
    /// </summary>
    private async Task<bool> HasHeldPaymentWithEvidenceBlockingExitAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        var svc = await (
                from e in db.ServiceEvidences.AsNoTracking()
                join sp in db.AgreementServicePayments.AsNoTracking()
                    on e.AgreementServicePaymentId equals sp.Id
                where sp.ThreadId == threadId
                      && sp.Status == AgreementServicePaymentStatuses.Held
                      && (e.Status == ServiceEvidenceStatuses.Submitted
                          || e.Status == ServiceEvidenceStatuses.Rejected)
                select e.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (svc)
            return true;

        return await (
                from e in db.MerchandiseEvidences.AsNoTracking()
                join ml in db.AgreementMerchandiseLinePaids.AsNoTracking()
                    on e.AgreementMerchandiseLinePaidId equals ml.Id
                where ml.ThreadId == threadId
                      && ml.Status == AgreementMerchandiseLinePaidStatuses.Held
                      && (e.Status == MerchandiseEvidenceStatuses.Submitted
                          || e.Status == MerchandiseEvidenceStatuses.Rejected)
                select e.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PartySoftLeavePaymentPrep> RefundHeldPaymentsForSellerExitAsync(
        string threadId,
        string storeId,
        CancellationToken cancellationToken)
    {
        var heldServices = await db.AgreementServicePayments
            .Where(x =>
                x.ThreadId == threadId
                && x.Status == AgreementServicePaymentStatuses.Held)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var heldMerch = await db.AgreementMerchandiseLinePaids
            .Where(x =>
                x.ThreadId == threadId
                && x.Status == AgreementMerchandiseLinePaidStatuses.Held)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (heldServices.Count == 0 && heldMerch.Count == 0)
            return new PartySoftLeavePaymentPrep(true, null, false, false, null);

        var cpIds = heldServices
            .Select(x => x.AgreementCurrencyPaymentId?.Trim())
            .Concat(heldMerch.Select(x => x.AgreementCurrencyPaymentId?.Trim()))
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

            var heldForCpServices = heldServices
                .Where(x =>
                    string.Equals(x.AgreementCurrencyPaymentId?.Trim(), cpId, StringComparison.Ordinal))
                .ToList();
            var heldForCpMerch = heldMerch
                .Where(x =>
                    string.Equals(x.AgreementCurrencyPaymentId?.Trim(), cpId, StringComparison.Ordinal))
                .ToList();
            var refundMinorTotal = heldForCpServices.Sum(x => x.AmountMinor) + heldForCpMerch.Sum(x => x.AmountMinor);
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

            foreach (var sp in heldForCpServices)
                sp.Status = AgreementServicePaymentStatuses.Refunded;
            foreach (var ml in heldForCpMerch)
                ml.Status = AgreementMerchandiseLinePaidStatuses.Refunded;
        }

        var refundNotice = await BuildSellerExitRefundNoticeAsync(heldServices, heldMerch, cancellationToken)
            .ConfigureAwait(false);

        await ApplyAggressiveStorePenaltyAsync(storeId, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PartySoftLeavePaymentPrep(true, null, true, true, refundNotice);
    }

    private async Task<string> BuildSellerExitRefundNoticeAsync(
        IReadOnlyList<AgreementServicePaymentRow> heldServices,
        IReadOnlyList<AgreementMerchandiseLinePaidRow> heldMerch,
        CancellationToken cancellationToken)
    {
        var ids = heldServices
            .Select(x => (x.TradeAgreementId ?? "").Trim())
            .Concat(heldMerch.Select(x => (x.TradeAgreementId ?? "").Trim()))
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

        var merchLineIds = heldMerch
            .Select(x => (x.MerchandiseLineId ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var merchDescById = merchLineIds.Count == 0
            ? new Dictionary<string, TradeAgreementMerchandiseLineRow>(StringComparer.Ordinal)
            : await db.TradeAgreementMerchandiseLines.AsNoTracking()
                .Where(l => merchLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine(
            "Los pagos retenidos en este chat fueron reembolsados al comprador por la salida del vendedor (acuerdos solo servicios o solo mercadería).");
        sb.AppendLine();

        foreach (var sp in heldServices
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
                $"• «{title}» · servicio · mes {sp.EntryMonth} día {sp.EntryDay} · {FormatServicePaymentAmountForNotice(sp)}");
        }

        foreach (var ml in heldMerch
                     .OrderBy(x => x.TradeAgreementId, StringComparer.Ordinal)
                     .ThenBy(x => x.MerchandiseLineId, StringComparer.Ordinal))
        {
            var aid = (ml.TradeAgreementId ?? "").Trim();
            var rawTitle = titleById.TryGetValue(aid, out var tt) && tt.Length > 0 ? tt : aid;
            var title = rawTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.Length == 0)
                title = aid;

            var lineKey = (ml.MerchandiseLineId ?? "").Trim();
            var lineLabel = "línea";
            if (merchDescById.TryGetValue(lineKey, out var lineRow))
            {
                var tipo = (lineRow.Tipo ?? "").Trim();
                var qty = (lineRow.Cantidad ?? "").Trim();
                lineLabel = tipo.Length > 0 ? $"{tipo} × {qty}" : lineKey;
            }

            sb.AppendLine(
                $"• «{title}» · mercadería · {lineLabel} · {FormatMerchandisePaymentAmountForNotice(ml)}");
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

    private static string FormatMerchandisePaymentAmountForNotice(AgreementMerchandiseLinePaidRow ml)
    {
        var curLower = PaymentCheckoutComputation.NormalizeCurrency(ml.Currency ?? "usd");
        if (curLower.Length == 0)
            curLower = "usd";

        var curUp = curLower.ToUpperInvariant();
        var pow = PaymentCheckoutComputation.StripeMinorDecimals(curLower);
        var major = pow == 0 ? ml.AmountMinor : ml.AmountMinor / 100m;
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
        storeRow.TrustScore = Math.Max(-10_000, prev - PartySoftLeaveTrust.SellerExitWithHeldRefundsPenalty);
        trustLedger.StageEntry(
            TrustLedgerSubjects.Store,
            sid,
            storeRow.TrustScore - prev,
            storeRow.TrustScore,
            "Salida del vendedor del chat con pagos retenidos reembolsados al comprador (servicios y/o mercadería).");
    }
}
