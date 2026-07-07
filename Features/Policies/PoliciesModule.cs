using VibeTrade.Backend.Features.Policies.Dtos;
using VibeTrade.Backend.Features.Policies.Interfaces;

namespace VibeTrade.Backend.Features.Policies;

public static class PoliciesModule
{
    public static IServiceCollection AddPoliciesFeature(this IServiceCollection services)
    {
        services.AddScoped<PartySoftLeaveCoordinator>();
        services.AddScoped<IChatExitOperationsService>(sp => sp.GetRequiredService<PartySoftLeaveCoordinator>());
        services.AddScoped<IChatExitPolicyRegistry>(sp => sp.GetRequiredService<PartySoftLeaveCoordinator>());
        services.AddScoped<IPartySoftLeaveCoordinator>(sp => sp.GetRequiredService<PartySoftLeaveCoordinator>());
        return services;
    }

    public static WebApplication MapPoliciesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/policies/chat").WithTags("Policies");

        group.MapPost("/threads/{threadId}/party-soft-leave", PostPartySoftLeaveAsync);
        group.MapPost("/threads/{threadId}/route-tramo-subscriptions/carrier-withdraw", PostCarrierWithdrawAsync);

        return app;
    }

    private static async Task<IResult> PostPartySoftLeaveAsync(
        string threadId,
        PartySoftLeaveBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatExitPolicyRegistry chatExitPolicyRegistry,
        IChatExitOperationsService chatExitOperations,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = (body?.Reason ?? "").Trim();
        if (r.Length < 1)
        {
            chatExitPolicyRegistry.TryMapPartySoftLeaveFailure("reason_required", out var st, out var msg);
            return Results.Json(new { error = "reason_required", message = msg }, statusCode: st);
        }

        var result = await chatExitOperations.PartySoftLeaveAsync(
                new PartySoftLeaveArgs(
                    userId,
                    threadId,
                    r,
                    (body?.TradeAgreementId ?? "").Trim() is { Length: >= 8 } aid ? aid : null),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var (status, errBody) = chatExitPolicyRegistry.PartySoftLeaveFailure(result.ErrorCode);
            return Results.Json(errBody, statusCode: status);
        }

        return Results.Ok(
            new PartySoftLeaveOkResponse(
                result.SkipClientTrustPenalty,
                result.OtherMemberCount,
                result.OtherMemberPenaltyApplied,
                result.TrustScoreAfterMemberPenalty));
    }

    private static async Task<IResult> PostCarrierWithdrawAsync(
        string threadId,
        CarrierWithdrawBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        IChatExitPolicyRegistry chatExitPolicyRegistry,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var agreementId = (body?.TradeAgreementId ?? "").Trim() is { Length: >= 8 } aid
            ? aid
            : null;
        var result = await routeTramoSubscriptions.WithdrawCarrierFromThreadAsync(
                userId,
                threadId,
                body?.Reason ?? "",
                agreementId,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
            return Results.NotFound(new { error = "not_found", message = "No hay suscripciones activas que retirar." });

        if (chatExitPolicyRegistry.TryMapCarrierWithdrawFailure(result.ErrorCode, out var carrierStatus, out var carrierMessage))
        {
            return Results.Json(
                new { error = result.ErrorCode, message = carrierMessage },
                statusCode: carrierStatus);
        }

        return Results.Ok(result);
    }
}
