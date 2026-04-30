using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat;

public sealed class AgreementCheckoutService(AppDbContext db, IChatService chat)
    : IAgreementCheckoutService
{
    /// <inheritdoc />
    public async Task<PaymentCheckoutComputation.BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
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

        return PaymentCheckoutComputation.ComputeForAgreement(ag, rp);
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

        var breakdown = PaymentCheckoutComputation.ComputeForAgreement(agr, rp);
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

        await chat.PostAutomatedPaymentFeeReceiptAsync(
            threadId,
            new ChatPaymentFeeReceiptPayload
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
            },
            cancellationToken).ConfigureAwait(false);
    }
}
