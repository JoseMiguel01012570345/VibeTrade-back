using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Stripe;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Chat;

internal static class AgreementCheckoutExecutor
{
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
        PaymentCheckoutComputation.CurrencyTotalsDto qb,
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
        PaymentCheckoutComputation.CurrencyTotalsDto qb)
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

    internal static AgreementExecutePaymentResultDto FromDup(AgreementCurrencyPaymentRow dup)
        => new(
            dup.StripePaymentIntentId ?? "",
            dup.Status == AgreementPaymentStatuses.Succeeded,
            dup.ClientSecretForConfirmation,
            dup.StripeErrorMessage,
            true,
            null,
            null);

    internal static async Task<AgreementExecutePaymentResultDto> PersistAndChargeAsync(
        AppDbContext db,
        AgreementCurrencyPaymentRow pay,
        PaymentCheckoutComputation.CurrencyTotalsDto qb,
        string paymentMethodId,
        string stripeCustomerId,
        CancellationToken ct)
    {
        if (PaymentStripeEnv.SkipStripePaymentIntentCreate())
        {
            var tail = pay.Id.Length >= 12 ? pay.Id.Substring(pay.Id.Length - 12) : pay.Id;
            pay.StripePaymentIntentId = $"skipped_{tail}";
            pay.Status = AgreementPaymentStatuses.Succeeded;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            AttachSplits(pay, qb);
            db.AgreementCurrencyPayments.Add(pay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Ok(pay.StripePaymentIntentId!, pay.Id);
        }

        var key = PaymentStripeEnv.StripeServerApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return Err("Falta configurar STRIPE_* en el servidor.", false, "stripe_not_configured");

        StripeConfiguration.ApiKey = key;

        PaymentMethod pm;
        try
        {
            pm = await new PaymentMethodService()
                .GetAsync(paymentMethodId, requestOptions: null, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (StripeException sx)
        {
            return Err(StripeMsg(sx), true, "stripe_pm_error");
        }

        var pcm = (pm.CustomerId ?? "").Trim();
        if (pcm.Length < 10 || !pcm.Equals(stripeCustomerId.Trim(), StringComparison.Ordinal))
            return Err("La tarjeta no pertenece a tu cliente Stripe.", false, "payment_method_not_owned");

        PaymentIntent pi;
        try
        {
            pi = await new PaymentIntentService().CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = qb.TotalMinor,
                    Currency = pay.Currency,
                    Customer = stripeCustomerId.Trim(),
                    PaymentMethod = paymentMethodId,
                    Confirm = true,
                    PaymentMethodTypes =
                    [
                        "card",
                    ],
                    Description = $"VibeTrade acuerdo {pay.TradeAgreementId}",
                },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (StripeException sx)
        {
            pay.Status = AgreementPaymentStatuses.Failed;
            pay.StripeErrorMessage = StripeMsg(sx);
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            db.AgreementCurrencyPayments.Add(pay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Err(pay.StripeErrorMessage, true, "stripe_charge_failed");
        }

        pay.StripePaymentIntentId = pi.Id ?? "";
        pay.ClientSecretForConfirmation =
            pi.Status is "requires_action" or "requires_confirmation" ? pi.ClientSecret : null;

        if (string.Equals(pi.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var estimatedStripe = qb.StripeFeeMinor;
            var actualStripe = estimatedStripe;
            try
            {
                var piFull = await new PaymentIntentService().GetAsync(
                    pi.Id,
                    new PaymentIntentGetOptions
                    {
                        Expand = new List<string> { "latest_charge.balance_transaction" },
                    },
                    requestOptions: null,
                    cancellationToken: ct).ConfigureAwait(false);
                var fee = piFull.LatestCharge?.BalanceTransaction?.Fee;
                if (fee is { } f && f >= 0)
                    actualStripe = f;
            }
            catch
            {
                // Sin balance_transaction: conservar estimación previa al cobro.
            }

            pay.StripeFeeAmountMinor = actualStripe;
            pay.Status = AgreementPaymentStatuses.Succeeded;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            AttachSplits(pay, qb);
            db.AgreementCurrencyPayments.Add(pay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Ok(pay.StripePaymentIntentId ?? "", pay.Id);
        }

        if (pi.Status is "requires_action" or "requires_confirmation")
        {
            pay.Status = AgreementPaymentStatuses.RequiresConfirmation;
            db.AgreementCurrencyPayments.Add(pay);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new AgreementExecutePaymentResultDto(pay.StripePaymentIntentId!, false,
                pay.ClientSecretForConfirmation, null,
                true, "requires_confirmation", null);
        }

        pay.Status = AgreementPaymentStatuses.Failed;
        pay.StripeErrorMessage = $"pi:{pi.Status}";
        pay.CompletedAtUtc = DateTimeOffset.UtcNow;
        db.AgreementCurrencyPayments.Add(pay);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Err(pay.StripeErrorMessage, false, "stripe_bad_status");
    }

    internal static AgreementExecutePaymentResultDto Ok(string paymentIntentId, string agreementCurrencyPaymentId) =>
        new(paymentIntentId, true, null, null, true, null, agreementCurrencyPaymentId);

    private static AgreementExecutePaymentResultDto Err(string msg, bool accepted, string code) =>
        new("", false, null, msg, accepted, code, null);

    private static string StripeMsg(StripeException sx) =>
        string.IsNullOrWhiteSpace(sx.StripeError?.Message) ? sx.Message : sx.StripeError!.Message;
}
