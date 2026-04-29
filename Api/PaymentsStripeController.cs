using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

[ApiController]
[Route("api/v1/payments/stripe")]
[Produces("application/json")]
[Tags("Payments")]
public sealed class PaymentsStripeController(IAuthService auth, AppDbContext db) : ControllerBase
{
    private static string? StripeSecretKey() => (Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? "").Trim() is { Length: > 0 } s ? s : null;
    private static string? StripePublishableKey() => (Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY") ?? "").Trim() is { Length: > 0 } s ? s : null;

    public sealed record StripeConfigDto(bool Enabled, string? PublishableKey);

    [HttpGet("config")]
    [ProducesResponseType(typeof(StripeConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetConfig()
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var secret = StripeSecretKey();
        var pub = StripePublishableKey();
        var enabled = secret is not null && pub is not null;
        return Ok(new StripeConfigDto(enabled, enabled ? pub : null));
    }

    public sealed record StripeCardPaymentMethodDto(
        string Id,
        string Brand,
        string Last4,
        int ExpMonth,
        int ExpYear);

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(List<StripeCardPaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var secret = StripeSecretKey();
        if (secret is null)
            return Ok(new List<StripeCardPaymentMethodDto>());

        var u = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId.Trim(), cancellationToken);
        var cusId = u?.StripeCustomerId?.Trim();
        if (string.IsNullOrWhiteSpace(cusId))
            return Ok(new List<StripeCardPaymentMethodDto>());

        StripeConfiguration.ApiKey = secret;
        // Usar el endpoint por customer y combinar filtros de allow_redisplay: en cuentas/API
        // recientes Stripe puede no devolver tarjetas con allow_redisplay=unspecified si solo
        // se lista sin filtro explícito (o viceversa). Unimos resultados únicos por id.
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
                var list = await cpmSvc.ListAsync(cusId, opts, requestOptions: null, cancellationToken: ct);
                var data = list.Data ?? new List<PaymentMethod>();
                foreach (var pm in data)
                    byId[pm.Id] = pm;
                if (!list.HasMore || data.Count == 0)
                    break;
                startingAfter = data[^1].Id;
            }
        }

        await AddAllPagesAsync(new CustomerPaymentMethodListOptions { Type = "card" }, cancellationToken);
        await AddAllPagesAsync(new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "always" }, cancellationToken);
        await AddAllPagesAsync(new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "limited" }, cancellationToken);
        await AddAllPagesAsync(new CustomerPaymentMethodListOptions { Type = "card", AllowRedisplay = "unspecified" }, cancellationToken);

        var outList = new List<StripeCardPaymentMethodDto>();
        foreach (var pm in byId.Values)
        {
            var c = pm.Card;
            if (c is null) continue;
            outList.Add(new StripeCardPaymentMethodDto(
                pm.Id,
                (c.Brand ?? "").Trim(),
                (c.Last4 ?? "").Trim(),
                (int)c.ExpMonth,
                (int)c.ExpYear));
        }
        return Ok(outList);
    }

    public sealed record CreateSetupIntentResult(string ClientSecret);

    [HttpPost("setup-intents")]
    [ProducesResponseType(typeof(CreateSetupIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostCreateSetupIntent(CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var secret = StripeSecretKey();
        if (secret is null)
            return BadRequest(new { error = "stripe_not_configured", message = "Falta STRIPE_SECRET_KEY en .env" });

        StripeConfiguration.ApiKey = secret;
        var (u, customerId) = await EnsureStripeCustomerAsync(userId.Trim(), cancellationToken);
        if (u is null || string.IsNullOrWhiteSpace(customerId))
            return BadRequest(new { error = "not_found", message = "Usuario no encontrado." });

        var setupSvc = new SetupIntentService();
        var si = await setupSvc.CreateAsync(
            new SetupIntentCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = new List<string> { "card" },
                Usage = "off_session",
                Metadata = new Dictionary<string, string>
                {
                    ["vibetradeUserId"] = userId.Trim(),
                },
            },
            requestOptions: null,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(si.ClientSecret))
            return BadRequest(new { error = "stripe_error", message = "Stripe no devolvió client_secret." });
        return Ok(new CreateSetupIntentResult(si.ClientSecret));
    }

    /// <param name="AmountMinor">Monto en unidad mínima de moneda (Stripe <c>amount</c>): p. ej. USD = centavos, 1000 = US$10,00.</param>
    public sealed record CreatePaymentIntentBody(long AmountMinor, string Currency, string? Description, string? PaymentMethodId);
    public sealed record CreatePaymentIntentResult(string ClientSecret);

    [HttpPost("payment-intents")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreatePaymentIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostCreatePaymentIntent(
        [FromBody] CreatePaymentIntentBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var secret = StripeSecretKey();
        if (secret is null)
            return BadRequest(new { error = "stripe_not_configured", message = "Falta STRIPE_SECRET_KEY en .env" });

        var cur = (body.Currency ?? "").Trim().ToLowerInvariant();
        if (cur.Length is < 3 or > 8)
            return BadRequest(new { error = "invalid_currency", message = "Moneda inválida." });
        if (body.AmountMinor <= 0)
            return BadRequest(new { error = "invalid_amount", message = "Monto inválido." });
        var pmId = (body.PaymentMethodId ?? "").Trim();
        if (pmId.Length == 0)
            return BadRequest(new { error = "missing_payment_method", message = "Seleccioná una tarjeta para pagar." });

        StripeConfiguration.ApiKey = secret;
        var (_, customerId) = await EnsureStripeCustomerAsync(userId.Trim(), cancellationToken);
        if (string.IsNullOrWhiteSpace(customerId))
            return BadRequest(new
            {
                error = "no_saved_cards",
                message = "No hay tarjetas configuradas. Agregá una tarjeta en Configurar antes de pagar.",
            });

        // Verificar que la tarjeta seleccionada pertenece al customer.
        var pmSvc = new PaymentMethodService();
        PaymentMethod? pm;
        try
        {
            pm = await pmSvc.GetAsync(pmId, requestOptions: null, cancellationToken: cancellationToken);
        }
        catch (StripeException)
        {
            return BadRequest(new { error = "invalid_payment_method", message = "La tarjeta seleccionada no es válida." });
        }
        var pmCustomer = (pm.CustomerId ?? pm.Customer?.Id ?? "").Trim();
        if (!string.Equals(pmCustomer, customerId, StringComparison.Ordinal))
            return BadRequest(new { error = "payment_method_not_owned", message = "La tarjeta seleccionada no pertenece a tu cuenta." });

        var svc = new PaymentIntentService();
        var pi = await svc.CreateAsync(
            new PaymentIntentCreateOptions
            {
                Amount = body.AmountMinor,
                Currency = cur,
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
                Metadata = new Dictionary<string, string>
                {
                    ["vibetradeUserId"] = userId.Trim(),
                },
                Customer = customerId,
                PaymentMethod = pmId,
                PaymentMethodTypes = new List<string> { "card" },
            },
            requestOptions: null,
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(pi.ClientSecret))
            return BadRequest(new { error = "stripe_error", message = "Stripe no devolvió client_secret." });

        return Ok(new CreatePaymentIntentResult(pi.ClientSecret));
    }

    private async Task<(UserAccount? user, string? customerId)> EnsureStripeCustomerAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var u = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (u is null)
            return (null, null);

        var existing = (u.StripeCustomerId ?? "").Trim();
        if (existing.Length > 0)
            return (u, existing);

        var createSvc = new CustomerService();
        var c = await createSvc.CreateAsync(
            new CustomerCreateOptions
            {
                Name = (u.DisplayName ?? "").Trim() is { Length: > 0 } dn ? dn : null,
                Email = (u.Email ?? "").Trim() is { Length: > 0 } em ? em : null,
                Metadata = new Dictionary<string, string> { ["vibetradeUserId"] = userId },
            },
            requestOptions: null,
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(c.Id))
            return (u, null);

        u.StripeCustomerId = c.Id.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return (u, u.StripeCustomerId);
    }
}

