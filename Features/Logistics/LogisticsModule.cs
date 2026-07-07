using VibeTrade.Backend.Features.Logistics.Interfaces;

namespace VibeTrade.Backend.Features.Logistics;

public static class LogisticsModule
{
    public static IServiceCollection AddLogisticsFeature(this IServiceCollection services)
    {
        services.AddScoped<ICarrierTelemetryService, CarrierTelemetryService>();
        services.AddScoped<ICarrierOwnershipService, CarrierOwnershipService>();
        services.AddScoped<ICarrierDeliveryEvidenceService, CarrierDeliveryEvidenceService>();
        services.AddScoped<ICarrierLegRefundService, CarrierLegRefundService>();
        services.AddScoped<ISellerRouteStopDeliveryCustodyService, SellerRouteStopDeliveryCustodyService>();
        services.AddScoped<IOrderRouteLifecycleService, OrderRouteLifecycleService>();
        services.AddHostedService<CarrierEvidenceDeadlineWatcher>();
        return services;
    }

    public static WebApplication MapRouteLogisticsEndpoints(this WebApplication app)
    {
        const string tag = "Chat logistics";

        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/telemetry", PostTelemetryAsync).WithTags(tag);
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/ownership/cede", PostCedeOwnershipAsync).WithTags(tag);
        app.MapGet("/api/v1/chat/threads/{threadId}/logistics/ownership/cede", GetCedeOwnershipAsync).WithTags(tag);
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/deliveries", ListDeliveriesAsync).WithTags(tag);
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/telemetry/latest", ListLatestTelemetryAsync).WithTags(tag);
        app.MapGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence", GetEvidenceAsync).WithTags(tag);
        app.MapPut("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence", UpsertEvidenceAsync).WithTags(tag);
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence/decide", DecideEvidenceAsync).WithTags(tag);
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/refunds/leg", RefundLegAsync).WithTags(tag);
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/deliveries/seller-pause", SellerPauseDeliveryAsync).WithTags(tag);
        app.MapPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/deliveries/seller-resume-from-idle", SellerResumeDeliveryAsync).WithTags(tag);

        return app;
    }

    private static async Task<IResult> PostTelemetryAsync(
        string threadId,
        string agreementId,
        PostTelemetryBody body,
        HttpRequest request,
        ICarrierTelemetryService telemetry,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = await telemetry.IngestAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                body.Lat,
                body.Lng,
                speedKmh: null,
                body.ReportedAtUtc,
                body.SourceClientId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (r is null)
            return Results.NotFound();

        if (!r.Accepted)
            return Results.BadRequest(r);

        return Results.Ok(r);
    }

    private static async Task<IResult> PostCedeOwnershipAsync(
        string threadId,
        string agreementId,
        CedeOwnershipBody body,
        HttpRequest request,
        ICarrierOwnershipService ownership,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = await ownership.CedeOwnershipAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (r is null)
            return Results.NotFound();

        if (!r.Ok)
            return Results.BadRequest(r);

        return Results.Ok(r);
    }

    private static async Task<IResult> GetCedeOwnershipAsync(
        string threadId,
        string routeSheetId,
        string routeStopId,
        HttpRequest request,
        ICarrierOwnershipService ownership,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = await ownership.GetCedeOwnershipAsync(
                userId.Trim(),
                threadId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (r is null)
            return Results.NotFound();

        return Results.Ok(r);
    }

    private static async Task<IResult> ListDeliveriesAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        ICarrierTelemetryService telemetry,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var rows = await telemetry.ListDeliveriesAsync(userId.Trim(), threadId.Trim(), agreementId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (rows is null)
            return Results.NotFound();

        return Results.Ok(rows);
    }

    private static async Task<IResult> ListLatestTelemetryAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        HttpRequest request,
        ICarrierTelemetryService telemetry,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var rows = await telemetry.ListLatestTelemetryForRouteSheetAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                (routeSheetId ?? "").Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (rows is null)
            return Results.NotFound();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetEvidenceAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        HttpRequest request,
        ICarrierDeliveryEvidenceService carrierEvidence,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (status, err, data) = await carrierEvidence.GetAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return Results.NotFound();
        if (status == StatusCodes.Status403Forbidden)
            return Results.Json(new { message = err }, statusCode: StatusCodes.Status403Forbidden);
        if (status != StatusCodes.Status200OK || data is null)
            return Results.BadRequest(new { message = err });

        return Results.Ok(data);
    }

    private static async Task<IResult> UpsertEvidenceAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        UpsertCarrierDeliveryEvidenceRequest body,
        HttpRequest request,
        ICarrierDeliveryEvidenceService carrierEvidence,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (status, err, data) = await carrierEvidence.UpsertAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return Results.NotFound();
        if (status == StatusCodes.Status403Forbidden)
            return Results.Json(new { message = err }, statusCode: StatusCodes.Status403Forbidden);
        if (status != StatusCodes.Status200OK)
            return Results.BadRequest(new { message = err });

        return Results.Ok(data);
    }

    private static async Task<IResult> DecideEvidenceAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        DecideCarrierDeliveryEvidenceRequest body,
        HttpRequest request,
        ICarrierDeliveryEvidenceService carrierEvidence,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (status, err) = await carrierEvidence.DecideAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return Results.NotFound();
        if (status == StatusCodes.Status403Forbidden)
            return Results.Json(new { message = err }, statusCode: StatusCodes.Status403Forbidden);
        if (status != StatusCodes.Status200OK)
            return Results.BadRequest(new { message = err });

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> RefundLegAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        HttpRequest request,
        ICarrierLegRefundService carrierLegRefund,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var (ok, code) = await carrierLegRefund.TryRefundEligibleLegAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok)
            return Results.BadRequest(new { error = code ?? "refund_failed" });

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SellerPauseDeliveryAsync(
        string threadId,
        string agreementId,
        SellerPauseDeliveryBody body,
        HttpRequest request,
        ISellerRouteStopDeliveryCustodyService sellerRouteCustody,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = await sellerRouteCustody.PauseForStoreCustodyAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                body.Reason,
                cancellationToken)
            .ConfigureAwait(false);
        if (!r.Ok)
            return Results.BadRequest(new { error = r.ErrorCode, message = r.Message });

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SellerResumeDeliveryAsync(
        string threadId,
        string agreementId,
        SellerResumeFromIdleBody body,
        HttpRequest request,
        ISellerRouteStopDeliveryCustodyService sellerRouteCustody,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var r = await sellerRouteCustody.ResumeFromIdleAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                body.TargetCarrierUserId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (!r.Ok)
            return Results.BadRequest(new { error = r.ErrorCode, message = r.Message });

        return Results.Ok(new { ok = true });
    }
}
