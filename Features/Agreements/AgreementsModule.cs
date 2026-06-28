using VibeTrade.Backend.Features.Agreements.Interfaces;

namespace VibeTrade.Backend.Features.Agreements;

public static class AgreementsModule
{
    public static IServiceCollection AddAgreementsFeature(this IServiceCollection services)
    {
        services.AddScoped<ITradeAgreementService, TradeAgreementService>();
        services.AddScoped<IAgreementServiceEvidenceService, AgreementServiceEvidenceService>();
        services.AddScoped<IAgreementMerchandiseEvidenceService, AgreementMerchandiseEvidenceService>();
        return services;
    }

    public static WebApplication MapChatAgreementsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/chat").WithTags("Chat", "Agreements");

        group.MapGet("/threads/{threadId}/trade-agreements", GetTradeAgreementsAsync);
        group.MapPost("/threads/{threadId}/trade-agreements", PostTradeAgreementAsync);
        group.MapPatch("/threads/{threadId}/trade-agreements/{agreementId}", PatchTradeAgreementAsync);
        group.MapPatch("/threads/{threadId}/trade-agreements/{agreementId}/route-link", PatchTradeAgreementRouteLinkAsync);
        group.MapPost("/threads/{threadId}/trade-agreements/{agreementId}/duplicate", PostDuplicateTradeAgreementAsync);
        group.MapPost("/threads/{threadId}/trade-agreements/{agreementId}/respond", PostTradeAgreementRespondAsync);
        group.MapDelete("/threads/{threadId}/trade-agreements/{agreementId}", DeleteTradeAgreementAsync);

        return app;
    }

    public static WebApplication MapChatAgreementEvidenceEndpoints(this WebApplication app)
    {
        var serviceGroup = app.MapGroup("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/service-payments")
            .WithTags("Chat", "Agreements");
        serviceGroup.MapGet("/", ListServicePaymentsAsync);
        serviceGroup.MapPut("/{paymentId}/evidence", UpsertServiceEvidenceAsync);
        serviceGroup.MapPost("/{paymentId}/evidence/decision", DecideServiceEvidenceAsync);
        serviceGroup.MapPost("/{paymentId}/seller-payout", RecordSellerPayoutAsync);

        var merchGroup = app.MapGroup("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/merchandise-line-payments")
            .WithTags("Chat", "Agreements");
        merchGroup.MapGet("/", ListMerchandiseLinePaymentsAsync);
        merchGroup.MapPut("/{paymentId}/evidence", UpsertMerchandiseEvidenceAsync);
        merchGroup.MapPost("/{paymentId}/evidence/decision", DecideMerchandiseEvidenceAsync);

        return app;
    }

    private static async Task<IResult> GetTradeAgreementsAsync(
        string threadId,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await tradeAgreements.ListForThreadAsync(userId, threadId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostTradeAgreementAsync(
        string threadId,
        TradeAgreementDraftRequest body,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (created, writeErr) = await tradeAgreements.CreateAsync(userId, threadId, body, cancellationToken);
        if (writeErr == TradeAgreementWriteErrors.DuplicateAgreementTitle)
            return Results.Conflict(new
            {
                error = writeErr,
                message = "En este chat ya hay un acuerdo con ese nombre. Elige otro título.",
            });
        if (writeErr == TradeAgreementWriteErrors.SingleAgreementCurrency)
            return Results.BadRequest(new
            {
                error = writeErr,
                message = AgreementCheckoutCurrency.MultipleAgreementCurrenciesMessage,
            });
        if (created is null)
            return Results.NotFound(new { error = "not_found", message = "No se pudo crear el acuerdo." });
        return Results.Ok(created);
    }

    private static async Task<IResult> PatchTradeAgreementAsync(
        string threadId,
        string agreementId,
        TradeAgreementDraftRequest body,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (updated, writeErr) = await tradeAgreements.UpdateAsync(userId, threadId, agreementId, body, cancellationToken);
        if (writeErr == TradeAgreementWriteErrors.DuplicateAgreementTitle)
            return Results.Conflict(new
            {
                error = writeErr,
                message = "En este chat ya hay otro acuerdo con ese nombre. Elige otro título.",
            });
        if (writeErr == TradeAgreementWriteErrors.SingleAgreementCurrency)
            return Results.BadRequest(new
            {
                error = writeErr,
                message = AgreementCheckoutCurrency.MultipleAgreementCurrenciesMessage,
            });
        if (updated is null)
            return Results.NotFound(new { error = "not_found", message = "No se pudo actualizar el acuerdo." });
        return Results.Ok(updated);
    }

    private static async Task<IResult> PatchTradeAgreementRouteLinkAsync(
        string threadId,
        string agreementId,
        TradeAgreementRouteLinkBody? body,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var outcome = await tradeAgreements.SetRouteSheetLinkAsync(
            userId,
            threadId,
            agreementId,
            body?.RouteSheetId,
            cancellationToken);
        if (outcome.Response is not null)
            return Results.Ok(outcome.Response);
        var code = outcome.FailureStatusCode ?? StatusCodes.Status404NotFound;
        var msg = outcome.FailureMessage ?? "No se pudo actualizar el vínculo con la hoja de ruta.";
        if (code == StatusCodes.Status400BadRequest)
            return Results.BadRequest(new { error = "no_merchandise", message = msg });
        if (code == StatusCodes.Status409Conflict)
            return Results.Conflict(new
            {
                error = TradeAgreementWriteErrors.RouteSheetAlreadyLinked,
                message = msg,
            });
        return Results.NotFound(new { error = "not_found", message = msg });
    }

    private static async Task<IResult> PostDuplicateTradeAgreementAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (created, writeErr) = await tradeAgreements.DuplicateAsync(
            userId,
            threadId,
            agreementId,
            cancellationToken);
        if (writeErr == TradeAgreementWriteErrors.DuplicateAgreementTitle)
            return Results.Conflict(new
            {
                error = writeErr,
                message = "En este chat ya hay un acuerdo con ese nombre. Elige otro título.",
            });
        if (created is null)
            return Results.NotFound(new { error = "not_found", message = "No se pudo duplicar el acuerdo." });
        return Results.Ok(created);
    }

    private static async Task<IResult> PostTradeAgreementRespondAsync(
        string threadId,
        string agreementId,
        TradeAgreementRespondBody body,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (updated, respondErr) = await tradeAgreements.RespondAsync(userId, threadId, agreementId, body.Accept, cancellationToken);
        if (respondErr == TradeAgreementWriteErrors.SingleAgreementCurrency)
            return Results.BadRequest(new
            {
                error = respondErr,
                message = AgreementCheckoutCurrency.MultipleAgreementCurrenciesMessage,
            });
        if (updated is null)
            return Results.NotFound(new { error = "not_found", message = "No se pudo registrar la respuesta." });
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteTradeAgreementAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        ITradeAgreementService tradeAgreements,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var ok = await tradeAgreements.DeleteAsync(userId, threadId, agreementId, cancellationToken);
        if (!ok)
            return Results.NotFound(new { error = "not_found", message = "No se pudo eliminar el acuerdo." });
        return Results.NoContent();
    }

    private static async Task<IResult> ListServicePaymentsAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        IAgreementServiceEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, data) = await svc.ListAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);
        return code == StatusCodes.Status200OK ? Results.Ok(data) : Results.StatusCode(code);
    }

    private static async Task<IResult> UpsertServiceEvidenceAsync(
        string threadId,
        string agreementId,
        string paymentId,
        UpsertServiceEvidenceRequest body,
        HttpRequest request,
        IAgreementServiceEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, err, data) = await svc.UpsertAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Results.Ok(data);
        return code == StatusCodes.Status400BadRequest ? Results.BadRequest(err) : Results.StatusCode(code);
    }

    private static async Task<IResult> DecideServiceEvidenceAsync(
        string threadId,
        string agreementId,
        string paymentId,
        DecideServiceEvidenceRequest body,
        HttpRequest request,
        IAgreementServiceEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, err) = await svc.DecideAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Results.Ok(new { ok = true });
        return code == StatusCodes.Status400BadRequest ? Results.BadRequest(err) : Results.StatusCode(code);
    }

    private static async Task<IResult> RecordSellerPayoutAsync(
        string threadId,
        string agreementId,
        string paymentId,
        RecordSellerServicePayoutRequest body,
        HttpRequest request,
        IAgreementServiceEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, err) = await svc.RecordSellerPayoutAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Results.Ok(new { ok = true });
        return code == StatusCodes.Status400BadRequest ? Results.BadRequest(err) : Results.StatusCode(code);
    }

    private static async Task<IResult> ListMerchandiseLinePaymentsAsync(
        string threadId,
        string agreementId,
        HttpRequest request,
        IAgreementMerchandiseEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, data) = await svc.ListAsync(userId, threadId, agreementId, cancellationToken)
            .ConfigureAwait(false);
        return code == StatusCodes.Status200OK ? Results.Ok(data) : Results.StatusCode(code);
    }

    private static async Task<IResult> UpsertMerchandiseEvidenceAsync(
        string threadId,
        string agreementId,
        string paymentId,
        UpsertMerchandiseEvidenceRequest body,
        HttpRequest request,
        IAgreementMerchandiseEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, err, data) = await svc.UpsertAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Results.Ok(data);
        return code == StatusCodes.Status400BadRequest ? Results.BadRequest(err) : Results.StatusCode(code);
    }

    private static async Task<IResult> DecideMerchandiseEvidenceAsync(
        string threadId,
        string agreementId,
        string paymentId,
        DecideMerchandiseEvidenceRequest body,
        HttpRequest request,
        IAgreementMerchandiseEvidenceService svc,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null) return Results.Unauthorized();
        var (code, err) = await svc.DecideAsync(userId, threadId, agreementId, paymentId, body, cancellationToken)
            .ConfigureAwait(false);
        if (code == StatusCodes.Status200OK) return Results.Ok(new { ok = true });
        return code == StatusCodes.Status400BadRequest ? Results.BadRequest(err) : Results.StatusCode(code);
    }
}
