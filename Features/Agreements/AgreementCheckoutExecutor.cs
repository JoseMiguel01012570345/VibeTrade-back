using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Payments.Interfaces;
using VibeTrade.Backend.Infrastructure.Stripe;

namespace VibeTrade.Backend.Features.Agreements;

/// <summary>
/// Resultado de resolver PaymentMethod Stripe de un customer.
/// <see cref="Accepted"/>: igual que cobro acuerdo, true si el error permite reintento (p. ej. PM).
/// </summary>
internal readonly record struct StripeCustomerPaymentMethodResolve(
    bool Success,
    string? PaymentMethodId,
    string? CardBrand,
    string? CardLast4,
    string? ErrorMessage,
    string? ErrorCode,
    bool Accepted);

internal static class AgreementCheckoutExecutor
{
    /// <summary>
    /// Clave servidor, GetAsync del PM y titularidad del PM respecto al customer.
    /// Misma secuencia que en <see cref="PersistAndChargeAsync"/> antes del PaymentIntent.
    /// </summary>
    internal static async Task<StripeCustomerPaymentMethodResolve> ResolveCustomerPaymentMethodAsync(
        IStripeGateway stripe,
        string paymentMethodId,
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        var resolved = await stripe.ResolveCustomerPaymentMethodAsync(
                paymentMethodId, stripeCustomerId, cancellationToken)
            .ConfigureAwait(false);
        return new StripeCustomerPaymentMethodResolve(
            resolved.Success,
            resolved.PaymentMethodId,
            resolved.CardBrand,
            resolved.CardLast4,
            resolved.ErrorMessage,
            resolved.ErrorCode,
            resolved.Accepted);
    }

    internal static async Task<TradeAgreementRow?> LoadAgreementAsync(
        AppDbContext db,
        string threadId,
        string agreementId,
        CancellationToken ct)
    {
        return await db.TradeAgreements.AsNoTracking().AsSplitQuery()
            .Where(a => a.Id == agreementId && a.ThreadId == threadId && a.DeletedAtUtc == null)
            .Include(a => a.MerchandiseLines)
            .Include(a => a.MerchandiseMeta)
            .Include(a => a.ServiceItems).ThenInclude(s => s.PaymentEntries)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    internal static async Task<RouteSheetPayload?> LoadRoutePayloadAsync(
        AppDbContext db,
        string threadId,
        string? routeSheetId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(routeSheetId)) return null;
        var row = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == threadId && x.RouteSheetId == routeSheetId && x.DeletedAtUtc == null,
                ct).ConfigureAwait(false);
        return row?.Payload;
    }

    internal static AgreementCurrencyPaymentRow NewRow(
        string threadId,
        string agreementId,
        string buyerUserId,
        string currencyLower,
        CurrencyTotalsDto qb,
        string paymentMethodStripeId,
        string? idempotencyKey)
        => new()
        {
            Id = $"agpay_{Guid.NewGuid():n}",
            TradeAgreementId = agreementId,
            ThreadId = threadId,
            BuyerUserId = buyerUserId,
            Currency = currencyLower,
            SubtotalAmountMinor = qb.SubtotalMinor,
            ClimateAmountMinor = qb.ClimateMinor,
            StripeFeeAmountMinor = qb.StripeFeeMinor,
            TotalAmountMinor = qb.TotalMinor,
            StripePaymentIntentId = null,
            Status = AgreementPaymentStatuses.Pending,
            PaymentMethodStripeId = paymentMethodStripeId,
            ClientIdempotencyKey =
                string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length < 8
                    ? null
                    : idempotencyKey.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    internal static void AttachSplits(AgreementCurrencyPaymentRow payment,
        CurrencyTotalsDto qb)
    {
        foreach (var ln in qb.Lines)
        {
            if (!string.Equals(ln.Category, "route_leg", StringComparison.Ordinal)) continue;

            string? sid = ln.RouteSheetId?.Trim();

            string? pid = ln.RouteStopId?.Trim();

            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(pid)) continue;

            payment.RouteLegPaids.Add(new AgreementRouteLegPaidRow
            {
                Id = $"agrl_{Guid.NewGuid():n}",
                AgreementCurrencyPaymentId = payment.Id,
                RouteSheetId = sid ?? "",
                RouteStopId = pid ?? "",
                AmountMinor = ln.AmountMinor,
            });
        }
    }

    internal static void AttachMerchandiseLineSplits(AgreementCurrencyPaymentRow payment,
        CurrencyTotalsDto qb)
    {
        foreach (var ln in qb.Lines)
        {
            if (!string.Equals(ln.Category, "merchandise", StringComparison.Ordinal)) continue;
            var mid = ln.MerchandiseLineId?.Trim();
            if (string.IsNullOrEmpty(mid)) continue;

            var now = DateTimeOffset.UtcNow;
            payment.MerchandiseLinePaids.Add(new AgreementMerchandiseLinePaidRow
            {
                Id = $"agml_{Guid.NewGuid():n}",
                AgreementCurrencyPaymentId = payment.Id,
                MerchandiseLineId = mid,
                Currency = ln.CurrencyLower.Trim().ToLowerInvariant(),
                AmountMinor = ln.AmountMinor,
                TradeAgreementId = payment.TradeAgreementId.Trim(),
                ThreadId = payment.ThreadId.Trim(),
                BuyerUserId = payment.BuyerUserId.Trim(),
                Status = AgreementMerchandiseLinePaidStatuses.Held,
                CreatedAtUtc = now,
            });
        }
    }

    internal static async Task AttachPendingMerchandiseEvidencesAsync(
        AppDbContext db,
        AgreementCurrencyPaymentRow payment,
        CancellationToken cancellationToken)
    {
        if (payment.MerchandiseLinePaids.Count == 0)
            return;

        var tid = payment.ThreadId.Trim();
        var sellerId = await db.ChatThreads.AsNoTracking()
            .Where(x => x.Id == tid)
            .Select(x => x.SellerUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sellerId))
            return;

        var now = DateTimeOffset.UtcNow;
        var uid = sellerId.Trim();
        foreach (var ml in payment.MerchandiseLinePaids)
        {
            db.MerchandiseEvidences.Add(new MerchandiseEvidenceRow
            {
                Id = $"mevd_{Guid.NewGuid():n}",
                AgreementMerchandiseLinePaidId = ml.Id,
                SellerUserId = uid,
                Status = MerchandiseEvidenceStatuses.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
    }

    internal static AgreementExecutePaymentResultDto FromDup(AgreementCurrencyPaymentRow dup)
        => new(
            dup.StripePaymentIntentId ?? "",
            dup.Status == AgreementPaymentStatuses.Succeeded,
            dup.ClientSecretForConfirmation,
            dup.StripeErrorMessage,
            true,
            null,
            dup.Id);

    /// <summary>
    /// Persist row; on unique idempotency race (parallel duplicate requests), detach and return the existing row result.
    /// </summary>
    private static async Task<AgreementExecutePaymentResultDto?> SavePaymentRowResolvingIdempotencyRaceAsync(
        AppDbContext db,
        AgreementCurrencyPaymentRow pay,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException ex) when (pay.ClientIdempotencyKey is { Length: >= 1 }
                                           && ex.InnerException is PostgresException pg
                                           && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            db.Entry(pay).State = EntityState.Detached;
            var dup = await db.AgreementCurrencyPayments.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.TradeAgreementId == pay.TradeAgreementId
                         && x.ClientIdempotencyKey == pay.ClientIdempotencyKey,
                    ct)
                .ConfigureAwait(false);
            if (dup is null)
                throw;
            return FromDup(dup);
        }
    }

    internal static async Task<AgreementExecutePaymentResultDto> PersistAndChargeAsync(
        AppDbContext db,
        IStripeGateway stripe,
        AgreementCurrencyPaymentRow pay,
        CurrencyTotalsDto qb,
        string paymentMethodId,
        string stripeCustomerId,
        CancellationToken ct)
    {
        // VIBETRADE_SKIP_PAYMENT_INTENTS=true en .env → cobro simulado sin Stripe (StripeEnv.SkipStripePaymentIntentCreate).
        if (stripe.SkipPaymentIntents)
        {
            var tail = pay.Id.Length >= 12 ? pay.Id.Substring(pay.Id.Length - 12) : pay.Id;
            pay.StripePaymentIntentId = $"skipped_{tail}";
            pay.Status = AgreementPaymentStatuses.Succeeded;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            AttachSplits(pay, qb);
            AttachMerchandiseLineSplits(pay, qb);
            await AttachPendingMerchandiseEvidencesAsync(db, pay, ct).ConfigureAwait(false);
            db.AgreementCurrencyPayments.Add(pay);
            var skipDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
            if (skipDup is not null)
                return skipDup;
            return Ok(pay.StripePaymentIntentId!, pay.Id);
        }

        var pmResolve = await ResolveCustomerPaymentMethodAsync(stripe, paymentMethodId, stripeCustomerId, ct)
            .ConfigureAwait(false);
        if (!pmResolve.Success)
            return Err(pmResolve.ErrorMessage!, pmResolve.Accepted, pmResolve.ErrorCode!);

        var charge = await stripe.CreateAndConfirmPaymentIntentAsync(
                stripeCustomerId,
                paymentMethodId,
                pay.TradeAgreementId,
                pay.Currency,
                qb.TotalMinor,
                ct)
            .ConfigureAwait(false);
        if (!charge.Success)
        {
            pay.Status = AgreementPaymentStatuses.Failed;
            pay.StripeErrorMessage = charge.ErrorMessage;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            db.AgreementCurrencyPayments.Add(pay);
            var chargeFailDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
            if (chargeFailDup is not null)
                return chargeFailDup;
            return Err(pay.StripeErrorMessage ?? "", charge.Accepted, charge.ErrorCode ?? "stripe_charge_failed");
        }

        pay.StripePaymentIntentId = charge.PaymentIntentId ?? "";
        pay.ClientSecretForConfirmation =
            charge.Status is "requires_action" or "requires_confirmation" ? charge.ClientSecret : null;

        if (string.Equals(charge.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var estimatedStripe = qb.StripeFeeMinor;
            var actualStripe = charge.ActualFeeMinor
                               ?? await stripe.GetActualStripeFeeMinorAsync(
                                   pay.StripePaymentIntentId, estimatedStripe, ct).ConfigureAwait(false)
                               ?? estimatedStripe;

            pay.StripeFeeAmountMinor = actualStripe;
            pay.Status = AgreementPaymentStatuses.Succeeded;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            AttachSplits(pay, qb);
            AttachMerchandiseLineSplits(pay, qb);
            await AttachPendingMerchandiseEvidencesAsync(db, pay, ct).ConfigureAwait(false);
            db.AgreementCurrencyPayments.Add(pay);
            var okDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
            if (okDup is not null)
                return okDup;
            return Ok(pay.StripePaymentIntentId ?? "", pay.Id);
        }

        if (charge.Status is "requires_action" or "requires_confirmation")
        {
            pay.Status = AgreementPaymentStatuses.RequiresConfirmation;
            db.AgreementCurrencyPayments.Add(pay);
            var confirmDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
            if (confirmDup is not null)
                return confirmDup;
            return new AgreementExecutePaymentResultDto(pay.StripePaymentIntentId!, false,
                pay.ClientSecretForConfirmation, null,
                true, "requires_confirmation", pay.Id);
        }

        pay.Status = AgreementPaymentStatuses.Failed;
        pay.StripeErrorMessage = $"pi:{charge.Status}";
        pay.CompletedAtUtc = DateTimeOffset.UtcNow;
        db.AgreementCurrencyPayments.Add(pay);
        var badDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
        if (badDup is not null)
            return badDup;

        return Err(pay.StripeErrorMessage, false, "stripe_bad_status");
    }

    internal static AgreementExecutePaymentResultDto Ok(string paymentIntentId, string agreementCurrencyPaymentId) =>
        new(paymentIntentId, true, null, null, true, null, agreementCurrencyPaymentId);

    private static AgreementExecutePaymentResultDto Err(string msg, bool accepted, string code) =>
        new("", false, null, msg, accepted, code, null);
}
