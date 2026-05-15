using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using Stripe;
using VibeTrade.Backend.Features.Payments;

namespace VibeTrade.Backend.Features.Payments;

public sealed class PaymentsService(
    AppDbContext db,
    IChatService chat,
    IChatThreadSystemMessageService threadSystemMessages,
    INotificationService notifications,
    IPaymentFeeReceiptEmailDispatcher paymentFeeReceiptEmail,
    ILogger<PaymentsService> logger)
    : IPaymentsService, IStripeUserPaymentService, IStripePaymentIntentService, IAgreementPaymentService
{
    private sealed record AgreementCheckoutTarget(
        string ThreadId,
        string AgreementId,
        string CurrencyLower,
        string PaymentMethodId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? ServicePicks,
        IReadOnlyList<string>? RouteStopPicks,
        IReadOnlyList<string>? MerchLinePicks);

    public StripeConfigDto GetStripeConfig()
    {
        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        var pub = PaymentStripeEnv.StripePublishableKey();
        var enabled = serverKey is not null && pub is not null;
        return new StripeConfigDto(enabled, enabled ? pub : null, PaymentStripeEnv.SkipStripePaymentIntentCreate());
    }

    public async Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        if (serverKey is null)
            return [];

        var u = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        var cusId = u?.StripeCustomerId?.Trim();
        if (string.IsNullOrWhiteSpace(cusId))
            return [];

        StripeConfiguration.ApiKey = serverKey;
        var byId = new Dictionary<string, PaymentMethod>(StringComparer.Ordinal);
        var cpmSvc = new CustomerPaymentMethodService();

        async Task AddAllPagesAsync(CustomerPaymentMethodListOptions template, CancellationToken ct)
        {
            string? startingAfter = null;
            while (true)
            {
                var opts = new CustomerPaymentMethodListOptions
                {
                    Type = template.Type,
                    Limit = 100,
                    StartingAfter = startingAfter,
                    AllowRedisplay = template.AllowRedisplay,
                };
                var list = await cpmSvc.ListAsync(cusId, opts, requestOptions: null, cancellationToken: ct)
                    .ConfigureAwait(false);
                var data = list.Data ?? new List<PaymentMethod>();
                foreach (var pm in data)
                    byId[pm.Id] = pm;
                if (!list.HasMore || data.Count == 0)
                    break;
                startingAfter = data[^1].Id;
            }
        }

        await AddAllPagesAsync(new CustomerPaymentMethodListOptions { Type = "card" }, cancellationToken);
        await AddAllPagesAsync(
            new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "always" },
            cancellationToken);
        await AddAllPagesAsync(
            new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "limited" },
            cancellationToken);
        await AddAllPagesAsync(
            new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "unspecified" },
            cancellationToken);

        var outList = new List<StripeCardPaymentMethodDto>();
        foreach (var pm in byId.Values)
        {
            var c = pm.Card;
            if (c is null) continue;
            var country = (c.Country ?? "").Trim().ToUpperInvariant();
            var countryOut = country.Length == 2 ? country : null;
            outList.Add(new StripeCardPaymentMethodDto(
                pm.Id,
                (c.Brand ?? "").Trim(),
                (c.Last4 ?? "").Trim(),
                (int)c.ExpMonth,
                (int)c.ExpYear,
                countryOut));
        }

        return outList;
    }

    public async Task<(bool Ok, object Problem, CreateSetupIntentResult? Data)> CreateSetupIntentAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        if (serverKey is null && !PaymentStripeEnv.SkipStripePaymentIntentCreate())
        {
            return (false,
                new
                {
                    error = "stripe_not_configured",
                    message = "Falta STRIPE_RESTRICTED_KEY o STRIPE_SECRET_KEY en .env",
                },
                null);
        }

        if (serverKey is not null)
            StripeConfiguration.ApiKey = serverKey;

        var (u, customerId) = await EnsureStripeCustomerAsync(userId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (u is null || string.IsNullOrWhiteSpace(customerId))
        {
            return (false, new { error = "not_found", message = "Usuario no encontrado." }, null);
        }

        if (PaymentStripeEnv.SkipStripePaymentIntentCreate())
        {
            // Integration / demo mode: persist a StripeCustomerId for flows that require it,
            // but do not call Stripe.
            return (true, null!, new CreateSetupIntentResult("seti_skip_" + Guid.NewGuid().ToString("N")));
        }

        var setupSvc = new SetupIntentService();
        var si = await setupSvc.CreateAsync(
                new SetupIntentCreateOptions
                {
                    Customer = customerId,
                    PaymentMethodTypes = new List<string> { "card" },
                    Usage = "off_session",
                    Metadata = new Dictionary<string, string> { ["vibetradeUserId"] = userId.Trim() },
                },
                requestOptions: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(si.ClientSecret))
        {
            return (false, new { error = "stripe_error", message = "Stripe no devolvió client_secret." }, null);
        }

        return (true, null!, new CreateSetupIntentResult(si.ClientSecret));
    }

    public async Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)> CreatePaymentIntentAsync(
        string userId,
        CreatePaymentIntentBody body,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                (body.Kind ?? "").Trim(),
                PaymentsStripePaymentKinds.AgreementCheckout,
                StringComparison.OrdinalIgnoreCase))
        {
            return Err(StatusCodes.Status400BadRequest, "invalid_kind", "Tipo de cobro no soportado o no indicado.");
        }

        if (!TryParseAgreementCheckoutTarget(body, out var target, out var err))
            return err;

        var buyer = userId.Trim();
        var (qbOrNull, totalsErr) =
            await ResolveAgreementCurrencyTotalsAsync(buyer, target, cancellationToken).ConfigureAwait(false);
        if (totalsErr is { } te)
            return te;
        var qb = qbOrNull!;

        var dupPi = await TryRejectDuplicateAgreementChargeAsync(
                target.AgreementId, target.CurrencyLower, target.ServicePicks, target.RouteStopPicks,
                target.MerchLinePicks, cancellationToken)
            .ConfigureAwait(false);
        if (dupPi is not null)
        {
            return (
                StatusCodes.Status400BadRequest,
                new { error = dupPi.ErrorCode ?? "duplicate", message = dupPi.StripeErrorMessage ?? "" },
                null);
        }

        if (PaymentStripeEnv.SkipStripePaymentIntentCreate())
        {
            return Ok(new CreatePaymentIntentResult("", PaymentSkipped: true, qb.TotalMinor, target.CurrencyLower));
        }

        return await CreateAgreementStripePaymentIntentAsync(buyer, target, qb, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaymentCheckoutComputation.BreakdownDto?> GetCheckoutBreakdownAsync(
        string buyerUserId,
        string threadId,
        string agreementId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
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

        var paidMerch =
            await LoadPaidMerchandiseLineIdsByCurrencyAsync(agreementId.Trim(), cancellationToken)
                .ConfigureAwait(false);

        return PaymentCheckoutComputation.ComputeForAgreement(ag, rp, selectedServicePayments, selectedRouteStopIds,
            selectedMerchandiseLineIds, paidMerch);
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadPaidMerchandiseLineIdsByCurrencyAsync(
        string agreementId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from ml in db.AgreementMerchandiseLinePaids.AsNoTracking()
                join cp in db.AgreementCurrencyPayments.AsNoTracking()
                    on ml.AgreementCurrencyPaymentId equals cp.Id
                where cp.TradeAgreementId == agreementId.Trim()
                      && cp.Status == AgreementPaymentStatuses.Succeeded
                      && ml.Status != AgreementMerchandiseLinePaidStatuses.Refunded
                      && ml.Status != AgreementMerchandiseLinePaidStatuses.Failed
                select new { ml.MerchandiseLineId, ml.Currency })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var cur = PaymentCheckoutComputation.NormalizeCurrency(row.Currency ?? "");
            var id = (row.MerchandiseLineId ?? "").Trim();
            if (cur.Length < 3 || id.Length == 0) continue;
            if (!map.TryGetValue(cur, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[cur] = set;
            }

            set.Add(id);
        }

        return map;
    }

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

    public async Task<AgreementExecutePaymentResultDto?> ExecuteCurrencyPaymentAsync(
        string buyerUserId, string threadId, string agreementId, string currencyLower,
        string paymentMethodStripeId, string? idempotencyKey,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds = null,
        CancellationToken cancellationToken = default)
    {
        var routeStopSummary = selectedRouteStopIds is null or { Count: 0 }
            ? "none"
            : string.Join(',', selectedRouteStopIds.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0));
        try
        {
            if (!await BuyerMayExecuteAgreementPaymentAsync(buyerUserId, threadId, cancellationToken).ConfigureAwait(false))
                return null;
            var cur = currencyLower.Trim().ToLowerInvariant();
            var pmId = paymentMethodStripeId.Trim();
            if (cur.Length is < 3 or > 8 || pmId.Length < 12)
                return ExecutePaymentErr.InvalidParams();
            var ik = (idempotencyKey ?? "").Trim();
            var blocked = await TryPreChargeExitAsync(ik, agreementId, cur, selectedServicePayments, selectedRouteStopIds,
                    selectedMerchandiseLineIds, cancellationToken)
                .ConfigureAwait(false);
            if (blocked is not null)
                return blocked;

            var agr = await AgreementCheckoutExecutor.LoadAgreementAsync(db, threadId.Trim(), agreementId.Trim(),
                cancellationToken).ConfigureAwait(false);
            if (agr is null)
                return ExecutePaymentErr.AgreementNotFound();
            var rp = await AgreementCheckoutExecutor.LoadRoutePayloadAsync(db, threadId.Trim(),
                agr.RouteSheetId?.Trim(), cancellationToken).ConfigureAwait(false);
            var paidMerch =
                await LoadPaidMerchandiseLineIdsByCurrencyAsync(agreementId.Trim(), cancellationToken)
                    .ConfigureAwait(false);
            var breakdown = PaymentCheckoutComputation.ComputeForAgreement(agr, rp, selectedServicePayments,
                selectedRouteStopIds, selectedMerchandiseLineIds, paidMerch);
            var qb = PaymentCheckoutComputation.GetCurrencyBucket(breakdown, cur);
            if (!breakdown.Ok || qb is null)
            {
                logger.LogWarning(
                    "ExecuteCurrencyPayment checkout invalid: thread={ThreadId} agreement={AgreementId} currency={Currency} routeStops={RouteStops} error={Error}",
                    threadId.Trim(), agreementId.Trim(), cur, routeStopSummary,
                    breakdown.Errors.FirstOrDefault() ?? "(none)");
                return ExecutePaymentErr.CheckoutInvalid(breakdown.Errors.FirstOrDefault());
            }

            var custId = await ResolveBuyerStripeCustomerIdAsync(buyerUserId.Trim(), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(custId))
            {
                logger.LogWarning(
                    "ExecuteCurrencyPayment stripe customer missing: buyer={BuyerId} thread={ThreadId} agreement={AgreementId}",
                    buyerUserId.Trim(), threadId.Trim(), agreementId.Trim());
                return ExecutePaymentErr.StripeNoCustomer();
            }

            logger.LogInformation(
                "ExecuteCurrencyPayment start: buyer={BuyerId} thread={ThreadId} agreement={AgreementId} currency={Currency} routeSheetId={RouteSheetId} routeStops={RouteStops} skipStripeIntents={Skip}",
                buyerUserId.Trim(), threadId.Trim(), agreementId.Trim(), cur,
                (agr.RouteSheetId ?? "").Trim(),
                routeStopSummary,
                PaymentStripeEnv.SkipStripePaymentIntentCreate());

            var pay = AgreementCheckoutExecutor.NewRow(threadId.Trim(), agr.Id.Trim(), buyerUserId.Trim(), cur, qb, pmId,
                ik.Length >= 8 ? ik : null);
            var result = await AgreementCheckoutExecutor.PersistAndChargeAsync(db, pay, qb, pmId, custId!, cancellationToken)
                .ConfigureAwait(false);
            if (result is { Succeeded: true, AgreementCurrencyPaymentId: { } pid })
            {
                logger.LogInformation(
                    "ExecuteCurrencyPayment charge ok, side-effects: paymentId={PaymentId} thread={ThreadId}",
                    pid, threadId.Trim());
                await PersistSucceededAgreementCurrencyPaymentSideEffectsAsync(threadId.Trim(), agr, buyerUserId.Trim(),
                    cur, rp, qb, pid, selectedServicePayments, selectedRouteStopIds, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logger.LogInformation(
                    "ExecuteCurrencyPayment finished without success side-effects: accepted={Accepted} thread={ThreadId} errorCode={ErrorCode} stripeMsg={StripeMsg}",
                    result.Accepted, threadId.Trim(), result.ErrorCode ?? "", result.StripeErrorMessage ?? "");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogExecuteCurrencyPaymentFailure(ex, buyerUserId, threadId, agreementId, currencyLower, routeStopSummary);
            throw;
        }
    }

    private void LogExecuteCurrencyPaymentFailure(
        Exception ex,
        string buyerUserId,
        string threadId,
        string agreementId,
        string currencyLower,
        string routeStopSummary)
    {
        var sb = new StringBuilder();
        sb.Append("ExecuteCurrencyPaymentAsync threw. ");
        for (Exception? e = ex; e is not null; e = e.InnerException)
            sb.Append('[').Append(e.GetType().Name).Append(": ").Append(e.Message).Append("] ");
        if (ex is DbUpdateException due)
        {
            foreach (var entry in due.Entries)
            {
                sb.Append("Entry=").Append(entry.Entity.GetType().Name).Append(" State=").Append(entry.State).Append("; ");
            }

            for (Exception? scan = due; scan is not null; scan = scan.InnerException)
            {
                if (scan is PostgresException pg)
                {
                    sb.Append("Postgres SqlState=").Append(pg.SqlState)
                        .Append(" Constraint=").Append(pg.ConstraintName ?? "")
                        .Append(" Detail=").Append(pg.Detail ?? "");
                    break;
                }
            }
        }

        logger.LogCritical(ex, "{Detail}", sb.ToString());
    }

    private async Task<bool> BuyerMayExecuteAgreementPaymentAsync(
        string buyerUserId, string threadId, CancellationToken cancellationToken)
    {
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId.Trim(), cancellationToken).ConfigureAwait(false);
        if (t is null || t.BuyerUserId != buyerUserId.Trim())
            return false;
        return await chat.UserCanAccessThreadRowAsync(buyerUserId.Trim(), t, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgreementExecutePaymentResultDto?> TryPreChargeExitAsync(
        string idempotencyKeyTrimmed,
        string agreementId,
        string currencyLower,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds,
        CancellationToken cancellationToken)
    {
        if (idempotencyKeyTrimmed.Length >= 8)
        {
            var prev = await db.AgreementCurrencyPayments.AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.ClientIdempotencyKey == idempotencyKeyTrimmed &&
                        x.TradeAgreementId == agreementId.Trim(),
                    cancellationToken).ConfigureAwait(false);
            if (prev is not null)
                return AgreementCheckoutExecutor.FromDup(prev);
        }

        return await TryRejectDuplicateAgreementChargeAsync(
            agreementId.Trim(), currencyLower, selectedServicePayments, selectedRouteStopIds,
            selectedMerchandiseLineIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AgreementExecutePaymentResultDto?> TryRejectDuplicateAgreementChargeAsync(
        string agreementId,
        string currencyLower,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        IReadOnlyList<string>? selectedMerchandiseLineIds,
        CancellationToken cancellationToken)
    {
        var svcPicks = selectedServicePayments?
            .Where(p =>
                p is not null
                && !string.IsNullOrWhiteSpace(p.ServiceItemId)
                && p.EntryMonth > 0
                && p.EntryDay > 0)
            .ToList() ?? [];

        var routePicks = selectedRouteStopIds?
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var merchPicks = selectedMerchandiseLineIds?
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (svcPicks is not { Count: > 0 } && routePicks is not { Count: > 0 } && merchPicks is not { Count: > 0 })
        {
            // Cobros "sin selección explícita" comparten moneda con pagos parciales por tramo/recurrencia.
            // Si ya hubo un cobro exitoso **con tramos** en esa moneda, no bloquear aquí: puede quedar mercadería u otros ítems.
            var existsGenericSucceeded = await db.AgreementCurrencyPayments.AsNoTracking().AnyAsync(
                x =>
                    x.TradeAgreementId == agreementId && x.Currency == currencyLower &&
                    x.Status == AgreementPaymentStatuses.Succeeded,
                cancellationToken).ConfigureAwait(false);
            if (!existsGenericSucceeded)
                return null;

            var existsSucceededWithoutRouteLegSplits = await (
                    from cp in db.AgreementCurrencyPayments.AsNoTracking()
                    where cp.TradeAgreementId == agreementId
                          && cp.Currency == currencyLower
                          && cp.Status == AgreementPaymentStatuses.Succeeded
                          && !db.AgreementRouteLegPaids.AsNoTracking().Any(rl => rl.AgreementCurrencyPaymentId == cp.Id)
                          && !db.AgreementServicePayments.AsNoTracking().Any(sp => sp.AgreementCurrencyPaymentId == cp.Id)
                          && !db.AgreementMerchandiseLinePaids.AsNoTracking()
                              .Any(ml => ml.AgreementCurrencyPaymentId == cp.Id)
                    select cp.Id)
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            return existsSucceededWithoutRouteLegSplits ? ExecutePaymentErr.AlreadyPaidCurrency() : null;
        }

        foreach (var pick in svcPicks)
        {
            var sid = pick.ServiceItemId.Trim();
            var dup = await (
                    from sp in db.AgreementServicePayments.AsNoTracking()
                    join cp in db.AgreementCurrencyPayments.AsNoTracking()
                        on sp.AgreementCurrencyPaymentId equals cp.Id
                    where sp.TradeAgreementId == agreementId
                          && sp.ServiceItemId == sid
                          && sp.EntryMonth == pick.EntryMonth
                          && sp.EntryDay == pick.EntryDay
                          && cp.Status == AgreementPaymentStatuses.Succeeded
                    select sp.Id)
                .AnyAsync(cancellationToken).ConfigureAwait(false);
            if (dup)
                return ExecutePaymentErr.RecurrenceAlreadyPaid();
        }

        if (routePicks is { Count: > 0 })
        {
            foreach (var stopId in routePicks)
            {
                var dupStop = await (
                        from rl in db.AgreementRouteLegPaids.AsNoTracking()
                        join cp in db.AgreementCurrencyPayments.AsNoTracking()
                            on rl.AgreementCurrencyPaymentId equals cp.Id
                        where cp.TradeAgreementId == agreementId
                              && rl.RouteStopId == stopId
                              && cp.Currency == currencyLower
                              && cp.Status == AgreementPaymentStatuses.Succeeded
                        select rl.Id)
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
                if (dupStop)
                    return ExecutePaymentErr.RouteStopAlreadyPaid();
            }
        }

        if (merchPicks is { Count: > 0 })
        {
            foreach (var lineId in merchPicks)
            {
                var dupMerch = await (
                        from ml in db.AgreementMerchandiseLinePaids.AsNoTracking()
                        join cp in db.AgreementCurrencyPayments.AsNoTracking()
                            on ml.AgreementCurrencyPaymentId equals cp.Id
                        where cp.TradeAgreementId == agreementId
                              && ml.MerchandiseLineId == lineId
                              && cp.Currency == currencyLower
                              && cp.Status == AgreementPaymentStatuses.Succeeded
                        select ml.Id)
                    .AnyAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (dupMerch)
                    return ExecutePaymentErr.MerchandiseLineAlreadyPaid();
            }
        }

        return null;
    }

    private async Task<string?> ResolveBuyerStripeCustomerIdAsync(string buyerUserId, CancellationToken cancellationToken)
    {
        var ua = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == buyerUserId, cancellationToken).ConfigureAwait(false);
        return ua?.StripeCustomerId?.Trim();
    }

    private async Task PersistSucceededAgreementCurrencyPaymentSideEffectsAsync(
        string threadId,
        TradeAgreementRow agr,
        string buyerUserId,
        string currencyLower,
        RouteSheetPayload? rp,
        PaymentCheckoutComputation.CurrencyTotalsDto qb,
        string agreementCurrencyPaymentId,
        IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? selectedServicePayments,
        IReadOnlyList<string>? selectedRouteStopIds,
        CancellationToken cancellationToken)
    {
        if (selectedServicePayments is { Count: > 0 })
        {
            var serviceRows = BuildServicePaymentRowsForSelection(
                threadId, agr, buyerUserId, currencyLower, agreementCurrencyPaymentId, selectedServicePayments);
            if (serviceRows.Count > 0)
            {
                db.AgreementServicePayments.AddRange(serviceRows);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await ApplyRouteStopDeliveryAfterSuccessfulPaymentAsync(threadId, agr, rp, qb, cancellationToken)
            .ConfigureAwait(false);

        await TryPostPaymentFeeReceiptMessageAsync(threadId, agr, rp, currencyLower, qb, agreementCurrencyPaymentId,
            cancellationToken).ConfigureAwait(false);
    }

    private static class ExecutePaymentErr
    {
        internal static AgreementExecutePaymentResultDto InvalidParams() =>
            new("", false, null, "Parámetros de pago incompletos.", false, "invalid_request");

        internal static AgreementExecutePaymentResultDto AgreementNotFound() =>
            new("", false, null, "Acuerdo no encontrado.", false, "not_found");

        internal static AgreementExecutePaymentResultDto CheckoutInvalid(string? detail) =>
            new("", false, null, detail ?? "No se pudo validar el desglose.", false, "checkout_invalid");

        internal static AgreementExecutePaymentResultDto StripeNoCustomer() =>
            new("", false, null, "Necesitas vincular tus tarjetas (cliente Stripe ausente).", false,
                "stripe_no_customer");

        internal static AgreementExecutePaymentResultDto AlreadyPaidCurrency() =>
            new("", false, null, "Ya existe un cobro exitoso para esa moneda.", false, "already_paid");

        internal static AgreementExecutePaymentResultDto RecurrenceAlreadyPaid() =>
            new("", false, null, "Esta recurrencia ya fue incluida en un cobro.", false, "recurrence_already_paid");

        internal static AgreementExecutePaymentResultDto RouteStopAlreadyPaid() =>
            new("", false, null, "Este tramo ya fue pagado en esta moneda.", false, "route_stop_already_paid");

        internal static AgreementExecutePaymentResultDto MerchandiseLineAlreadyPaid() =>
            new("", false, null, "Esta línea de mercadería ya fue cobrada en esta moneda.", false,
                "merchandise_line_already_paid");
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

    private static IReadOnlyList<string>? MapRouteStopPicks(IReadOnlyList<string>? ids)
    {
        if (ids is null) return null;
        return ids
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string>? MapMerchandiseLinePicks(IReadOnlyList<string>? ids)
    {
        if (ids is null) return null;
        return ids
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task ApplyRouteStopDeliveryAfterSuccessfulPaymentAsync(
        string threadId,
        TradeAgreementRow agr,
        RouteSheetPayload? rp,
        PaymentCheckoutComputation.CurrencyTotalsDto qb,
        CancellationToken cancellationToken)
    {
        var rsid = (agr.RouteSheetId ?? "").Trim();
        if (rsid.Length == 0 || rp?.Paradas is not { Count: > 0 } paradas)
            return;

        var routeLines = qb.Lines
            .Where(l =>
                string.Equals(l.Category, "route_leg", StringComparison.Ordinal)
                && string.Equals((l.RouteSheetId ?? "").Trim(), rsid, StringComparison.Ordinal))
            .ToList();
        if (routeLines.Count == 0)
            return;

        var paidInThisCharge = routeLines
            .Select(l => (l.RouteStopId ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (paidInThisCharge.Count == 0)
            return;

        var orderedStopIds = paradas
            .OrderBy(p => p.Orden)
            .Select(p => (p.Id ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();

        var subs = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId.Trim()
                && x.RouteSheetId == rsid
                && x.Status == "confirmed")
            .Select(x => new { x.StopId, x.CarrierUserId, x.CreatedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var carrierByStop = subs
            .GroupBy(x => (x.StopId ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(x => x.CreatedAtUtc)
                    .ThenBy(x => (x.CarrierUserId ?? "").Trim(), StringComparer.Ordinal)
                    .Select(x => (x.CarrierUserId ?? "").Trim())
                    .First(),
                StringComparer.Ordinal);

        var existing = await db.RouteStopDeliveries
            .Where(x =>
                x.ThreadId == threadId.Trim()
                && x.TradeAgreementId == agr.Id.Trim()
                && x.RouteSheetId == rsid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byStop = existing.ToDictionary(x => x.RouteStopId.Trim(), StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var firstPaidStopId = RouteLegOwnershipChain.FirstPaidStopId(orderedStopIds, paidInThisCharge);

        foreach (var stopId in orderedStopIds.Where(paidInThisCharge.Contains))
        {
            if (!byStop.TryGetValue(stopId, out var row))
            {
                row = new RouteStopDeliveryRow
                {
                    Id = "rsd_" + Guid.NewGuid().ToString("N"),
                    ThreadId = threadId.Trim(),
                    TradeAgreementId = agr.Id.Trim(),
                    RouteSheetId = rsid,
                    RouteStopId = stopId,
                    State = RouteStopDeliveryStates.Unpaid,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.RouteStopDeliveries.Add(row);
                byStop[stopId] = row;
            }

            if (row.State == RouteStopDeliveryStates.Unpaid
                || row.State == RouteStopDeliveryStates.AwaitingCarrierForHandoff)
            {
                row.UpdatedAtUtc = now;

                var carrier = carrierByStop.TryGetValue(stopId, out var c) ? c : "";
                if (carrier.Length >= 2)
                {
                    var grantOwnerHere = firstPaidStopId is not null
                        && string.Equals(stopId, firstPaidStopId, StringComparison.Ordinal);
                    if (grantOwnerHere)
                    {
                        if (!string.Equals(row.CurrentOwnerUserId, carrier, StringComparison.Ordinal))
                        {
                            row.CurrentOwnerUserId = carrier;
                            row.OwnershipGrantedAtUtc = now;
                            db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
                            {
                                Id = "coe_" + Guid.NewGuid().ToString("N"),
                                ThreadId = threadId.Trim(),
                                RouteSheetId = rsid,
                                RouteStopId = stopId,
                                CarrierUserId = carrier,
                                Action = CarrierOwnershipActions.Granted,
                                AtUtc = now,
                                Reason = "payment_success",
                            });
                        }

                        row.State = RouteStopDeliveryStates.AwaitingCarrierForHandoff;
                        row.UpdatedAtUtc = now;
                    }
                    else
                    {
                        /* Tramos siguientes pagados: sin titular hasta cadena (ceder / evidencia en tramo previo). */
                        row.CurrentOwnerUserId = null;
                        row.OwnershipGrantedAtUtc = null;
                        row.State = RouteStopDeliveryStates.AwaitingCarrierForHandoff;
                        row.UpdatedAtUtc = now;
                    }
                }
                else
                {
                    row.CurrentOwnerUserId = null;
                    row.OwnershipGrantedAtUtc = null;
                    row.State = RouteStopDeliveryStates.AwaitingCarrierForHandoff;
                    row.UpdatedAtUtc = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (rp is not null)
        {
            await RouteLegHandoffNotifications.NotifyPaidStopsAsync(
                    db,
                    notifications,
                    threadId.Trim(),
                    agr.Id.Trim(),
                    rsid,
                    rp,
                    paidInThisCharge,
                    cancellationToken)
                .ConfigureAwait(false);
        }
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

        var storeDisplayName = "";
        var threadRow = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId.Trim(), cancellationToken).ConfigureAwait(false);
        var sid = (threadRow?.StoreId ?? "").Trim();
        if (sid.Length >= 2)
        {
            var st = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken).ConfigureAwait(false);
            storeDisplayName = (st?.Name ?? "").Trim();
        }

        var estimated = qb.StripeFeeMinor;

        var lines = qb.Lines.Select(l => new ChatPaymentFeeReceiptLineDto
        {
            Label = l.Label,
            AmountMinor = l.AmountMinor,
        }).ToList();

        var receiptPayload = new ChatPaymentFeeReceiptData
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
            InvoiceIssuerPlatform = "VibeTrade",
            InvoiceStoreName = storeDisplayName,
        };

        await threadSystemMessages.PostAutomatedPaymentFeeReceiptAsync(
            threadId,
            receiptPayload,
            cancellationToken).ConfigureAwait(false);

        await paymentFeeReceiptEmail.TryDispatchToThreadParticipantsAsync(
            threadId,
            receiptPayload,
            cancellationToken).ConfigureAwait(false);
    }

    private static (int StatusCode, object? Problem, CreatePaymentIntentResult? Data) Err(
        int status,
        string code,
        string message) =>
        (status, new { error = code, message }, null);

    private static (int StatusCode, object? Problem, CreatePaymentIntentResult? Data) Ok(
        CreatePaymentIntentResult data) =>
        (StatusCodes.Status200OK, null, data);

    private static bool TryParseAgreementCheckoutTarget(
        CreatePaymentIntentBody body,
        [NotNullWhen(true)] out AgreementCheckoutTarget? target,
        out (int StatusCode, object? Problem, CreatePaymentIntentResult? Data) err)
    {
        var threadId = (body.ThreadId ?? "").Trim();
        var agreementId = (body.AgreementId ?? "").Trim();
        var cur = (body.Currency ?? "").Trim().ToLowerInvariant();
        var pmId = (body.PaymentMethodId ?? "").Trim();

        if (threadId.Length < 8 || agreementId.Length < 8)
        {
            err = Err(StatusCodes.Status400BadRequest, "invalid_target", "Indicá hilo y acuerdo válidos para el cobro.");
            target = null;
            return false;
        }

        if (cur.Length is < 3 or > 8)
        {
            err = Err(StatusCodes.Status400BadRequest, "invalid_currency", "Moneda inválida.");
            target = null;
            return false;
        }

        if (pmId.Length == 0)
        {
            err = Err(StatusCodes.Status400BadRequest, "missing_payment_method", "Selecciona una tarjeta para pagar.");
            target = null;
            return false;
        }

        target = new AgreementCheckoutTarget(
            threadId,
            agreementId,
            cur,
            pmId,
            MapServicePicks(body.SelectedServicePayments),
            MapRouteStopPicks(body.SelectedRouteStopIds),
            MapMerchandiseLinePicks(body.SelectedMerchandiseLineIds));
        err = default;
        return true;
    }

    private async Task<(
        PaymentCheckoutComputation.CurrencyTotalsDto? Qb,
        (int StatusCode, object? Problem, CreatePaymentIntentResult? Data)? Error)> ResolveAgreementCurrencyTotalsAsync(
        string buyerUserId,
        AgreementCheckoutTarget t,
        CancellationToken cancellationToken)
    {
        var breakdown = await GetCheckoutBreakdownAsync(buyerUserId, t.ThreadId, t.AgreementId, t.ServicePicks,
                t.RouteStopPicks, t.MerchLinePicks, cancellationToken)
            .ConfigureAwait(false);
        if (breakdown is null)
        {
            return (null, Err(StatusCodes.Status404NotFound, "not_found", "No se encontró el acuerdo o no tenés acceso."));
        }

        if (!breakdown.Ok)
        {
            var msg = breakdown.Errors.FirstOrDefault() ?? "No se pudo validar el desglose.";
            return (null, Err(StatusCodes.Status400BadRequest, "checkout_invalid", msg));
        }

        var qb = PaymentCheckoutComputation.GetCurrencyBucket(breakdown, t.CurrencyLower);
        if (qb is null || qb.TotalMinor <= 0)
        {
            return (
                null,
                Err(
                    StatusCodes.Status400BadRequest,
                    "invalid_amount",
                    "No hay importe a cobrar para esa moneda en el acuerdo."));
        }

        return (qb, null);
    }

    private async Task<(int StatusCode, object? Problem, CreatePaymentIntentResult? Data)>
        CreateAgreementStripePaymentIntentAsync(
            string buyerUserId,
            AgreementCheckoutTarget t,
            PaymentCheckoutComputation.CurrencyTotalsDto qb,
            CancellationToken cancellationToken)
    {
        var serverKey = PaymentStripeEnv.StripeServerApiKey();
        if (serverKey is null)
        {
            return Err(
                StatusCodes.Status400BadRequest,
                "stripe_not_configured",
                "Falta STRIPE_RESTRICTED_KEY o STRIPE_SECRET_KEY en .env");
        }

        StripeConfiguration.ApiKey = serverKey;
        var (_, customerId) = await EnsureStripeCustomerAsync(buyerUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Err(
                StatusCodes.Status400BadRequest,
                "no_saved_cards",
                "No hay tarjetas configuradas. Añade una tarjeta en Configurar antes de pagar.");
        }

        PaymentMethod pm;
        try
        {
            pm = await new PaymentMethodService()
                .GetAsync(t.PaymentMethodId, requestOptions: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StripeException)
        {
            return Err(
                StatusCodes.Status400BadRequest,
                "invalid_payment_method",
                "La tarjeta seleccionada no es válida.");
        }

        var pmCustomer = (pm.CustomerId ?? pm.Customer?.Id ?? "").Trim();
        if (!string.Equals(pmCustomer, customerId, StringComparison.Ordinal))
        {
            return Err(
                StatusCodes.Status400BadRequest,
                "payment_method_not_owned",
                "La tarjeta seleccionada no pertenece a tu cuenta.");
        }

        var pi = await new PaymentIntentService().CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = qb.TotalMinor,
                    Currency = t.CurrencyLower,
                    Description = $"VibeTrade acuerdo {t.AgreementId}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["vibetradeUserId"] = buyerUserId,
                        ["threadId"] = t.ThreadId,
                        ["agreementId"] = t.AgreementId,
                    },
                    Customer = customerId,
                    PaymentMethod = t.PaymentMethodId,
                    PaymentMethodTypes = new List<string> { "card" },
                },
                requestOptions: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(pi.ClientSecret))
        {
            return Err(StatusCodes.Status400BadRequest, "stripe_error", "Stripe no devolvió client_secret.");
        }

        return Ok(new CreatePaymentIntentResult(pi.ClientSecret, false, qb.TotalMinor, t.CurrencyLower));
    }

    private static IReadOnlyList<PaymentCheckoutComputation.ServicePaymentPickDto>? MapServicePicks(
        IReadOnlyList<AgreementCheckoutPaymentIntentItemDto>? items)
    {
        if (items is not { Count: > 0 }) return null;
        var list = new List<PaymentCheckoutComputation.ServicePaymentPickDto>();
        foreach (var x in items)
        {
            var sid = (x.ServiceItemId ?? "").Trim();
            if (sid.Length == 0 || x.EntryMonth <= 0 || x.EntryDay <= 0) continue;
            list.Add(new PaymentCheckoutComputation.ServicePaymentPickDto(sid, x.EntryMonth, x.EntryDay));
        }

        return list.Count > 0 ? list : null;
    }

    private async Task<(UserAccount? user, string? customerId)> EnsureStripeCustomerAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var u = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (u is null)
            return (null, null);

        var existing = (u.StripeCustomerId ?? "").Trim();
        if (existing.Length > 0)
            return (u, existing);

        if (PaymentStripeEnv.SkipStripePaymentIntentCreate())
        {
            u.StripeCustomerId = "cus_test_skip_" + Guid.NewGuid().ToString("N")[..16];
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (u, u.StripeCustomerId);
        }

        var createSvc = new CustomerService();
        var c = await createSvc.CreateAsync(
                new CustomerCreateOptions
                {
                    Name = (u.DisplayName ?? "").Trim() is { Length: > 0 } dn ? dn : null,
                    Email = (u.Email ?? "").Trim() is { Length: > 0 } em ? em : null,
                    Metadata = new Dictionary<string, string> { ["vibetradeUserId"] = userId },
                },
                requestOptions: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(c.Id))
            return (u, null);

        u.StripeCustomerId = c.Id.Trim();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (u, u.StripeCustomerId);
    }
}
