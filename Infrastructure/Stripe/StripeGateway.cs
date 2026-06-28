using Stripe;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.Payments.Dtos;

namespace VibeTrade.Backend.Infrastructure.Stripe;

public sealed class StripeGateway : IStripeGateway
{
    public bool SkipPaymentIntents => StripeEnv.SkipStripePaymentIntentCreate();

    public StripeConfigDto GetConfig()
    {
        var serverKey = StripeEnv.StripeServerApiKey();
        var pub = StripeEnv.StripePublishableKey();
        var enabled = serverKey is not null && pub is not null;
        return new StripeConfigDto(enabled, enabled ? pub : null, SkipPaymentIntents);
    }

    public string GenerateSkipModeCustomerId() => "cus_test_skip_" + Guid.NewGuid().ToString("N")[..16];

    public string GenerateSkipModeSetupIntentId() => "seti_skip_" + Guid.NewGuid().ToString("N");

    public async Task<string?> CreateCustomerAsync(
        string userId,
        string? displayName,
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _))
            return null;

        var createSvc = new CustomerService();
        var c = await createSvc.CreateAsync(
                new CustomerCreateOptions
                {
                    Name = (displayName ?? "").Trim() is { Length: > 0 } dn ? dn : null,
                    Email = (email ?? "").Trim() is { Length: > 0 } em ? em : null,
                    Metadata = new Dictionary<string, string> { ["vibetradeUserId"] = userId },
                },
                requestOptions: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(c.Id) ? null : c.Id.Trim();
    }

    public async Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var cusId = customerId.Trim();
        if (SkipPaymentIntents)
        {
            if (!string.IsNullOrWhiteSpace(cusId))
            {
                return
                [
                    new StripeCardPaymentMethodDto(
                        StripeEnv.DemoSkipPaymentMethodId,
                        "visa",
                        "4242",
                        12,
                        2034,
                        "US"),
                ];
            }

            return [];
        }

        if (!TryConfigureApiKey(out _))
            return [];

        if (string.IsNullOrWhiteSpace(cusId))
            return [];

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

    public async Task<StripePaymentMethodResolve> ResolveCustomerPaymentMethodAsync(
        string paymentMethodId,
        string customerId,
        CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _))
        {
            return new StripePaymentMethodResolve(
                false, null, null, null,
                "Falta configurar STRIPE_* en el servidor.", "stripe_not_configured",
                Accepted: false);
        }

        PaymentMethod pm;
        try
        {
            pm = await new PaymentMethodService()
                .GetAsync(paymentMethodId, requestOptions: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StripeException sx)
        {
            return new StripePaymentMethodResolve(
                false, null, null, null,
                AgreementUtils.StripeErrorUserMessage(sx), "stripe_pm_error",
                Accepted: true);
        }

        var pcm = (pm.CustomerId ?? "").Trim();
        if (pcm.Length < 10 || !pcm.Equals(customerId.Trim(), StringComparison.Ordinal))
        {
            return new StripePaymentMethodResolve(
                false, null, null, null,
                "La tarjeta no pertenece a tu cliente Stripe.", "payment_method_not_owned",
                Accepted: false);
        }

        var card = pm.Card;
        return new StripePaymentMethodResolve(
            true,
            pm.Id,
            card is null ? null : (card.Brand ?? "").Trim(),
            card is null ? null : (card.Last4 ?? "").Trim(),
            null,
            null,
            false);
    }

    public async Task<(bool Ok, string? ErrorMessage, CreateSetupIntentResult? Result)> CreateSetupIntentAsync(
        string customerId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _) && !SkipPaymentIntents)
            return (false, "Falta STRIPE_RESTRICTED_KEY o STRIPE_SECRET_KEY en .env", null);

        if (SkipPaymentIntents)
            return (true, null, new CreateSetupIntentResult(GenerateSkipModeSetupIntentId()));

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
            return (false, "Stripe no devolvió client_secret.", null);

        return (true, null, new CreateSetupIntentResult(si.ClientSecret));
    }

    public async Task<(bool Ok, string? ErrorCode, string? ErrorMessage, CreatePaymentIntentResult? Result)>
        CreateCheckoutPaymentIntentAsync(
            string buyerUserId,
            string customerId,
            string paymentMethodId,
            string threadId,
            string agreementId,
            string currencyLower,
            long amountMinor,
            CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _))
        {
            return (false, "stripe_not_configured",
                "Falta STRIPE_RESTRICTED_KEY o STRIPE_SECRET_KEY en .env", null);
        }

        var pmResolve = await ResolveCustomerPaymentMethodAsync(paymentMethodId, customerId, cancellationToken)
            .ConfigureAwait(false);
        if (!pmResolve.Success)
        {
            return (false,
                pmResolve.ErrorCode ?? "invalid_payment_method",
                pmResolve.ErrorMessage ?? "La tarjeta seleccionada no es válida.",
                null);
        }

        PaymentIntent pi;
        try
        {
            pi = await new PaymentIntentService().CreateAsync(
                    new PaymentIntentCreateOptions
                    {
                        Amount = amountMinor,
                        Currency = currencyLower,
                        Description = $"VibeTrade acuerdo {agreementId}",
                        Metadata = new Dictionary<string, string>
                        {
                            ["vibetradeUserId"] = buyerUserId,
                            ["threadId"] = threadId,
                            ["agreementId"] = agreementId,
                        },
                        Customer = customerId,
                        PaymentMethod = paymentMethodId,
                        PaymentMethodTypes = new List<string> { "card" },
                    },
                    requestOptions: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StripeException)
        {
            return (false, "invalid_payment_method", "La tarjeta seleccionada no es válida.", null);
        }

        if (string.IsNullOrWhiteSpace(pi.ClientSecret))
            return (false, "stripe_error", "Stripe no devolvió client_secret.", null);

        return (true, null, null,
            new CreatePaymentIntentResult(pi.ClientSecret, false, amountMinor, currencyLower));
    }

    public async Task<StripeChargeResult> CreateAndConfirmPaymentIntentAsync(
        string customerId,
        string paymentMethodId,
        string agreementId,
        string currency,
        long amountMinor,
        CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _))
        {
            return new StripeChargeResult(
                false, null, null, null,
                "Falta configurar STRIPE_* en el servidor.", "stripe_not_configured",
                Accepted: false, null);
        }

        PaymentIntent pi;
        try
        {
            pi = await new PaymentIntentService().CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = amountMinor,
                    Currency = currency,
                    Customer = customerId.Trim(),
                    PaymentMethod = paymentMethodId,
                    Confirm = true,
                    PaymentMethodTypes = ["card"],
                    Description = $"VibeTrade acuerdo {agreementId}",
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException sx)
        {
            return new StripeChargeResult(
                false, null, null, null,
                AgreementUtils.StripeErrorUserMessage(sx), "stripe_charge_failed",
                Accepted: true, null);
        }

        var status = pi.Status ?? "";
        var clientSecret = pi.Status is "requires_action" or "requires_confirmation" ? pi.ClientSecret : null;
        long? actualFee = null;
        if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pi.Id))
            actualFee = await GetActualStripeFeeMinorAsync(pi.Id, amountMinor, cancellationToken).ConfigureAwait(false);

        return new StripeChargeResult(
            true,
            pi.Id ?? "",
            clientSecret,
            status,
            null,
            null,
            Accepted: true,
            actualFee);
    }

    public async Task<long?> GetActualStripeFeeMinorAsync(
        string paymentIntentId,
        long estimatedFeeMinor,
        CancellationToken cancellationToken = default)
    {
        if (!TryConfigureApiKey(out _))
            return estimatedFeeMinor;

        try
        {
            var piFull = await new PaymentIntentService().GetAsync(
                paymentIntentId,
                new PaymentIntentGetOptions
                {
                    Expand = new List<string> { "latest_charge.balance_transaction" },
                },
                requestOptions: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var fee = piFull.LatestCharge?.BalanceTransaction?.Fee;
            if (fee is { } f && f >= 0)
                return f;
        }
        catch
        {
            // Sin balance_transaction: conservar estimación previa al cobro.
        }

        return estimatedFeeMinor;
    }

    private static bool TryConfigureApiKey(out string? key)
    {
        key = StripeEnv.StripeServerApiKey();
        if (key is null)
            return false;
        StripeConfiguration.ApiKey = key;
        return true;
    }
}
