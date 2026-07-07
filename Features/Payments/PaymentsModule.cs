using VibeTrade.Backend.Features.Payments.Gateways;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsFeature(this IServiceCollection services)
    {
        services.AddSingleton<SimulatedPaymentGateway>();
        services.AddSingleton<IPaymentGatewayManager, PaymentGatewayManager>();
        services.AddScoped<PaymentsServiceCore>();
        services.AddScoped<PaymentsService>();
        services.AddScoped<IPaymentsService, PaymentsService>();
        services.AddScoped<IRoutePathCheckoutQueryService, RoutePathCheckoutQueryService>();
        services.AddScoped<IAgreementPaymentService, PaymentsService>();
        services.AddScoped<IPaymentFeeReceiptEmailDispatcher, PaymentFeeReceiptEmailDispatcher>();
        return services;
    }

    public static WebApplication MapPaymentsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/payments/gateway/config", GetPaymentGatewayConfig).WithTags("Payments");
        app.MapGet("/api/v1/payments/gateway/payment-methods", GetPaymentMethodsAsync).WithTags("Payments");
        app.MapPost("/api/v1/payments/gateway/setup-intents", PostSetupIntentAsync).WithTags("Payments");
        app.MapPost("/api/v1/payments/gateway/payment-intents", PostPaymentIntentAsync).WithTags("Payments");
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/checkout", GetAgreementCheckoutAsync).WithTags("Payments");
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/payments", ListAgreementPaymentsAsync).WithTags("Payments");
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/route-paths", GetAgreementRoutePathsAsync).WithTags("Payments");
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/checkout-breakdown", PostAgreementCheckoutBreakdownAsync).WithTags("Payments");
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/payments/execute", ExecuteAgreementPaymentAsync).WithTags("Payments");

        return app;
    }

    private static IResult GetPaymentGatewayConfig(HttpRequest request, IPaymentsService payments, ICurrentUserAccessor currentUser)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        return Results.Ok(payments.GetPaymentGatewayConfig());
    }

    private static async Task<IResult> GetPaymentMethodsAsync(
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var list = await payments.ListCardPaymentMethodsAsync(userId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostSetupIntentAsync(
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (ok, problem, data) =
            await payments.CreateSetupIntentAsync(userId.Trim(), cancellationToken).ConfigureAwait(false);
        if (!ok || data is null)
            return Results.BadRequest(problem);
        return Results.Ok(data);
    }

    private static async Task<IResult> PostPaymentIntentAsync(
        CreatePaymentIntentBody body,
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (status, problem, data) =
            await payments.CreatePaymentIntentAsync(userId.Trim(), body, cancellationToken)
                .ConfigureAwait(false);
        if (status != StatusCodes.Status200OK || data is null)
            return Results.Json(problem, statusCode: status);
        return Results.Ok(data);
    }

    private static async Task<IResult> GetAgreementCheckoutAsync(
        string threadId,
        string agreementId,
        string? routePathId,
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        IReadOnlyList<string>? routePaths = null;
        var pathFilter = (routePathId ?? "").Trim();
        if (pathFilter.Length > 0)
            routePaths = [pathFilter];

        var bd = await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null,
                routePaths, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return Results.NotFound();
        return Results.Ok(bd);
    }

    private static async Task<IResult> ListAgreementPaymentsAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        if (await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, null, null, cancellationToken)
                .ConfigureAwait(false)
            is null)
            return Results.NotFound();

        var list = await payments.ListPaymentStatusesAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(list);
    }

    private static async Task<IResult> GetAgreementRoutePathsAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        HttpRequest request,
        IRoutePathCheckoutQueryService routePathCheckout,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(routeSheetId))
            return Results.BadRequest(new { error = "missing_route_sheet_id", message = "Indica routeSheetId." });

        var dto = await routePathCheckout
            .GetAgreementRoutePathsAsync(userId, threadId, agreementId, routeSheetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (dto is null)
            return Results.NotFound();
        return Results.Ok(dto);
    }

    private static async Task<IResult> PostAgreementCheckoutBreakdownAsync(
        string threadId,
        string agreementId,
        CheckoutBreakdownBody body,
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var picks = body.SelectedServicePayments?
            .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
            .Select(x => new ServicePaymentPickDto(
                x.ServiceItemId.Trim(),
                x.EntryMonth,
                x.EntryDay))
            .ToList();

        var routePaths = body.SelectedRoutePathIds is null ? null : body.SelectedRoutePathIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var bd = await payments.GetCheckoutBreakdownAsync(userId, threadId, agreementId, picks,
                routePaths, cancellationToken)
            .ConfigureAwait(false);
        if (bd is null)
            return Results.NotFound();
        return Results.Ok(bd);
    }

    private static async Task<IResult> ExecuteAgreementPaymentAsync(
        string threadId,
        string agreementId,
        ExecutePaymentBody body,
        HttpRequest request,
        IPaymentsService payments,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var headerKey = (request.Headers["Idempotency-Key"].FirstOrDefault() ?? "").Trim();
        var idem = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? headerKey : body.IdempotencyKey!.Trim();

        var routePathsExec = body.SelectedRoutePathIds is null ? null : body.SelectedRoutePathIds
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var r = await payments.ExecuteCurrencyPaymentAsync(
            userId,
            threadId,
            agreementId,
            body.Currency,
            body.PaymentMethodId,
            idem.Length >= 8 ? idem : null,
            body.SelectedServicePayments?
                .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItemId))
                .Select(x => new ServicePaymentPickDto(
                    x.ServiceItemId.Trim(),
                    x.EntryMonth,
                    x.EntryDay))
                .ToList(),
            routePathsExec,
            cancellationToken).ConfigureAwait(false);

        if (r is null)
            return Results.NotFound();

        if (!r.Accepted)
            return Results.BadRequest(r);

        return Results.Ok(r);
    }
}
