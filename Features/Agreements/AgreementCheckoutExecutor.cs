using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Payments.Gateways;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Agreements;

/// <summary>
/// Resultado de resolver un método de pago del comprador.
/// <see cref="Accepted"/>: true si el error permite reintento (p. ej. PM).
/// </summary>
internal readonly record struct CustomerPaymentMethodResolve(
    bool Success,
    string? PaymentMethodId,
    string? CardBrand,
    string? CardLast4,
    string? ErrorMessage,
    string? ErrorCode,
    bool Accepted);

internal static class AgreementCheckoutExecutor
{
    internal static CustomerPaymentMethodResolve ResolveCustomerPaymentMethod(
        SimulatedPaymentGateway gateway,
        string paymentMethodId,
        string payerAccountId)
    {
        var resolved = gateway.ResolvePaymentMethod(paymentMethodId, payerAccountId);
        return new CustomerPaymentMethodResolve(
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
        string paymentMethodId,
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
            ProcessorFeeAmountMinor = qb.ProcessorFeeMinor,
            TotalAmountMinor = qb.TotalMinor,
            GatewayTransactionId = null,
            Status = AgreementPaymentStatuses.Pending,
            PaymentMethodId = paymentMethodId,
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

    internal static AgreementExecutePaymentResultDto FromDup(AgreementCurrencyPaymentRow dup)
        => new(
            dup.GatewayTransactionId ?? "",
            dup.Status == AgreementPaymentStatuses.Succeeded,
            dup.ClientSecretForConfirmation,
            dup.PaymentErrorMessage,
            true,
            null,
            dup.Id);

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
        IPaymentGatewayManager gatewayManager,
        SimulatedPaymentGateway simulatedGateway,
        AgreementCurrencyPaymentRow pay,
        CurrencyTotalsDto qb,
        string paymentMethodId,
        string payerAccountId,
        CancellationToken ct)
    {
        var pmResolve = ResolveCustomerPaymentMethod(simulatedGateway, paymentMethodId, payerAccountId);
        if (!pmResolve.Success)
            return Err(pmResolve.ErrorMessage!, pmResolve.Accepted, pmResolve.ErrorCode!);

        var gateway = gatewayManager.GetGateway();
        var transfer = await gateway.TransferAsync(
                new PaymentTransferRequest(
                    payerAccountId,
                    SimulatedPaymentGateway.PlatformAccountId,
                    pay.Currency,
                    qb.TotalMinor,
                    Description: $"VibeTrade acuerdo {pay.TradeAgreementId}",
                    IdempotencyKey: pay.ClientIdempotencyKey,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["agreement_id"] = pay.TradeAgreementId,
                        ["thread_id"] = pay.ThreadId,
                        ["buyer_user_id"] = pay.BuyerUserId,
                        ["payment_method_id"] = pmResolve.PaymentMethodId ?? paymentMethodId,
                    }),
                ct)
            .ConfigureAwait(false);

        if (!transfer.Success)
        {
            pay.Status = AgreementPaymentStatuses.Failed;
            pay.PaymentErrorMessage = transfer.ErrorMessage;
            pay.CompletedAtUtc = DateTimeOffset.UtcNow;
            db.AgreementCurrencyPayments.Add(pay);
            var chargeFailDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
            if (chargeFailDup is not null)
                return chargeFailDup;
            return Err(
                pay.PaymentErrorMessage ?? "No se pudo completar el cobro.",
                true,
                transfer.ErrorCode ?? "payment_charge_failed");
        }

        pay.GatewayTransactionId = transfer.TransactionId ?? "";
        pay.ProcessorFeeAmountMinor = qb.ProcessorFeeMinor;
        pay.Status = AgreementPaymentStatuses.Succeeded;
        pay.CompletedAtUtc = DateTimeOffset.UtcNow;
        AttachSplits(pay, qb);
        db.AgreementCurrencyPayments.Add(pay);
        var okDup = await SavePaymentRowResolvingIdempotencyRaceAsync(db, pay, ct).ConfigureAwait(false);
        if (okDup is not null)
            return okDup;
        return Ok(pay.GatewayTransactionId ?? "", pay.Id);
    }

    internal static AgreementExecutePaymentResultDto Ok(string transactionId, string agreementCurrencyPaymentId) =>
        new(transactionId, true, null, null, true, null, agreementCurrencyPaymentId);

    private static AgreementExecutePaymentResultDto Err(string msg, bool accepted, string code) =>
        new("", false, null, msg, accepted, code, null);
}
