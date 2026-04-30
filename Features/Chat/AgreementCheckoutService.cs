using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat;

public sealed class AgreementCheckoutService(
    AppDbContext db,
    IChatService chat,
    IPaymentFeeReceiptEmailDispatcher paymentFeeReceiptEmail)
    : IAgreementCheckoutService
{
    /// <inheritdoc />
    public async Task<PaymentCheckoutComputation.BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.BuyerUserId != buyerUserId.Trim()) return null;
        if (!await chat.UserCanAccessThreadRowAsync(buyerUserId.Trim(), t, cancellationToken)
                .ConfigureAwait(false))
            return null;

        var ag =
            await AgreementCheckoutExecutor.LoadAgreementAsync(db, threadId.Trim(), agreementId.Trim(),
                cancellationToken).ConfigureAwait(false);
        if (ag is null) return null;

        var rp = await AgreementCheckoutExecutor.LoadRoutePayloadAsync(db, threadId.Trim(), ag.RouteSheetId?.Trim(),
            cancellationToken).ConfigureAwait(false);

        return PaymentCheckoutComputation.ComputeForAgreement(ag, rp, selectedServicePayments);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgreementPaymentStatusDto>> ListPaymentStatusesAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.BuyerUserId != buyerUserId.Trim()) return [];
        if (!await chat.UserCanAccessThreadRowAsync(buyerUserId.Trim(), t, cancellationToken)
                .ConfigureAwait(false))
            return [];

        return await db.AgreementCurrencyPayments.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId.Trim() && x.TradeAgreementId == agreementId.Trim())
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x =>
                new AgreementPaymentStatusDto(x.Currency, x.Status, x.TotalAmountMinor,
                    x.StripePaymentIntentId ?? "", x.CompletedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AgreementExecutePaymentResultDto?> ExecuteCurrencyPaymentAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        string currencyLower,
        string paymentMethodStripeId,
        string? idempotencyKey,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        CancellationToken cancellationToken = default)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (t is null || t.BuyerUserId != buyerUserId.Trim()) return null;
        if (!await chat.UserCanAccessThreadRowAsync(buyerUserId.Trim(), t, cancellationToken)
                .ConfigureAwait(false))
            return null;

        var cur = currencyLower.Trim().ToLowerInvariant();
        var pmId = paymentMethodStripeId.Trim();
        if (cur.Length is < 3 or > 8 || pmId.Length < 12)
            return new AgreementExecutePaymentResultDto("", false, null,
                "Parámetros de pago incompletos.", false, "invalid_request");

        var ik = (idempotencyKey ?? "").Trim();
        if (ik.Length >= 8)
        {
            var prev = await db.AgreementCurrencyPayments.AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.ClientIdempotencyKey == ik && x.TradeAgreementId == agreementId.Trim(),
                    cancellationToken).ConfigureAwait(false);

            if (prev is not null)
                return AgreementCheckoutExecutor.FromDup(prev);
        }

        if (await db.AgreementCurrencyPayments.AsNoTracking().AnyAsync(
                x =>
                    x.TradeAgreementId == agreementId.Trim() && x.Currency == cur &&
                    x.Status == AgreementPaymentStatuses.Succeeded,
                cancellationToken).ConfigureAwait(false))
            return new AgreementExecutePaymentResultDto("", false, null,
                "Ya existe un cobro exitoso para esa moneda.", false, "already_paid");

        TradeAgreementRow? agr =
            await AgreementCheckoutExecutor.LoadAgreementAsync(db, threadId.Trim(),
                agreementId.Trim(), cancellationToken).ConfigureAwait(false);

        if (agr is null)
            return new AgreementExecutePaymentResultDto("", false, null, "Acuerdo no encontrado.", false,
                "not_found");

        var rp =
            await AgreementCheckoutExecutor.LoadRoutePayloadAsync(db, threadId.Trim(),
                agr.RouteSheetId?.Trim(),
                cancellationToken).ConfigureAwait(false);

        var breakdown = PaymentCheckoutComputation.ComputeForAgreement(agr, rp, selectedServicePayments);
        var qb = PaymentCheckoutComputation.GetCurrencyBucket(breakdown, cur);
        if (!breakdown.Ok || qb is null)
            return new AgreementExecutePaymentResultDto("", false, null,
                breakdown.Errors.FirstOrDefault() ?? "No se pudo validar el desglose.",
                false,
                "checkout_invalid");

        UserAccount? ua =
            await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == buyerUserId.Trim(),
                cancellationToken).ConfigureAwait(false);

        var custId = ua?.StripeCustomerId?.Trim();

        if (string.IsNullOrEmpty(custId))
            return new AgreementExecutePaymentResultDto("", false, null,
                "Necesitas vincular tus tarjetas (cliente Stripe ausente).",
                false,
                "stripe_no_customer");

        AgreementCurrencyPaymentRow pay = AgreementCheckoutExecutor.NewRow(threadId.Trim(),
            agr.Id.Trim(), buyerUserId.Trim(),
            cur,
            qb, pmId,
            ik.Length >= 8 ? ik : null);

        var result = await AgreementCheckoutExecutor.PersistAndChargeAsync(db, pay, qb, pmId, custId!, cancellationToken)
            .ConfigureAwait(false);

        if (result is { Succeeded: true, AgreementCurrencyPaymentId: { } pid })
        {
            if (selectedServicePayments is { Count: > 0 })
            {
                var serviceRows = BuildServicePaymentRowsForSelection(
                    threadId.Trim(),
                    agr,
                    buyerUserId.Trim(),
                    cur,
                    pid,
                    selectedServicePayments);
                if (serviceRows.Count > 0)
                {
                    db.AgreementServicePayments.AddRange(serviceRows);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            await TryPostPaymentFeeReceiptMessageAsync(
                threadId.Trim(),
                agr,
                rp,
                cur,
                qb,
                pid,
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static List<AgreementServicePaymentRow> BuildServicePaymentRowsForSelection(
        string threadId,
        TradeAgreementRow agr,
        string buyerUserId,
        string currencyLower,
        string agreementCurrencyPaymentId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto> picks)
    {
        var now = DateTimeOffset.UtcNow;
        var outList = new List<AgreementServicePaymentRow>();
        foreach (var svc in agr.ServiceItems.OrderBy(x => x.SortOrder))
        {
            if (!svc.Configured) continue;
            var sid = (svc.Id ?? "").Trim();
            if (sid.Length == 0) continue;
            foreach (var pick in picks.Where(p => string.Equals(p.ServiceItemId?.Trim(), sid, StringComparison.Ordinal)))
            {
                if (pick.EntryMonth <= 0 || pick.EntryDay <= 0) continue;
                var entry = svc.PaymentEntries
                    .OrderBy(e => e.SortOrder)
                    .FirstOrDefault(e => e.Month == pick.EntryMonth && e.Day == pick.EntryDay);
                if (entry is null) continue;
                var mon = (entry.Moneda ?? "").Trim().ToLowerInvariant();
                if (!string.Equals(mon, currencyLower.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    continue;
                var rawAmt = (entry.Amount ?? "").Trim().Replace(",", ".", StringComparison.Ordinal).Replace('\u00a0', ' ');
                if (!decimal.TryParse(rawAmt, System.Globalization.CultureInfo.InvariantCulture, out var amtMajor) || amtMajor <= 0)
                {
                    continue;
                }
                var amtMinor = PaymentCheckoutComputation.MajorToMinor(amtMajor, mon);
                if (amtMinor <= 0) continue;
                outList.Add(new AgreementServicePaymentRow
                {
                    Id = $"agsp_{Guid.NewGuid():n}",
                    TradeAgreementId = agr.Id.Trim(),
                    ThreadId = threadId,
                    BuyerUserId = buyerUserId,
                    ServiceItemId = sid,
                    EntryMonth = pick.EntryMonth,
                    EntryDay = pick.EntryDay,
                    Currency = currencyLower.Trim().ToLowerInvariant(),
                    AmountMinor = amtMinor,
                    Status = AgreementServicePaymentStatuses.Held,
                    AgreementCurrencyPaymentId = agreementCurrencyPaymentId,
                    CreatedAtUtc = now,
                });
            }
        }
        return outList;
    }

    private async Task TryPostPaymentFeeReceiptMessageAsync(
        string threadId,
        TradeAgreementRow agr,
        RouteSheetPayload? routePayload,
        string currencyLower,
        PaymentCheckoutComputation.CurrencyTotalsDto qb,
        string paymentId,
        CancellationToken cancellationToken)
    {
        var payRow = await db.AgreementCurrencyPayments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == paymentId, cancellationToken).ConfigureAwait(false);
        if (payRow is null)
            return;

        var breakdown = PaymentCheckoutComputation.ComputeForAgreement(agr, routePayload);
        var qbFresh = PaymentCheckoutComputation.GetCurrencyBucket(breakdown, currencyLower);
        var estimated = qbFresh?.StripeFeeMinor ?? qb.StripeFeeMinor;

        var lines = qb.Lines.Select(l => new ChatPaymentFeeReceiptLineDto
        {
            Label = l.Label,
            AmountMinor = l.AmountMinor,
        }).ToList();

        var receiptPayload = new ChatPaymentFeeReceiptPayload
        {
            AgreementId = agr.Id.Trim(),
            AgreementTitle = (agr.Title ?? "").Trim(),
            PaymentId = payRow.Id,
            CurrencyLower = currencyLower,
            SubtotalMinor = payRow.SubtotalAmountMinor,
            ClimateMinor = payRow.ClimateAmountMinor,
            StripeFeeMinorActual = payRow.StripeFeeAmountMinor,
            StripeFeeMinorEstimated = estimated,
            TotalChargedMinor = payRow.TotalAmountMinor,
            StripePricingUrl = StripePricingLinks.PricingPage,
            Lines = lines,
        };

        await chat.PostAutomatedPaymentFeeReceiptAsync(
            threadId,
            receiptPayload,
            cancellationToken).ConfigureAwait(false);

        await paymentFeeReceiptEmail.TryDispatchToThreadParticipantsAsync(
            threadId,
            receiptPayload,
            cancellationToken).ConfigureAwait(false);
    }
}
