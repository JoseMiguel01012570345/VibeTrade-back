using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Policies.Interfaces;

namespace VibeTrade.Backend.Features.Chat;

public static partial class ChatModule
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/chat").WithTags("Chat");

        group.MapPost("/threads", PostThreadAsync);
        group.MapPost("/threads/social-group", PostSocialGroupThreadAsync);
        group.MapPost("/threads/support", PostSupportThreadAsync);
        group.MapGet("/threads", GetThreadsAsync);
        group.MapPost("/ack-pending-delivery-on-login", PostAckPendingDeliveryOnLoginAsync);
        group.MapGet("/threads/by-offer/{offerId}", GetThreadByOfferAsync);
        group.MapPost("/threads/{threadId}/notify-participant-left", PostNotifyParticipantLeftAsync);
        group.MapDelete("/threads/{threadId}", DeleteThreadAsync);
        group.MapGet("/threads/{threadId}", GetThreadAsync);
        group.MapGet("/threads/{threadId}/members", GetSocialThreadMembersAsync);
        group.MapPatch("/threads/{threadId}/social-title", PatchSocialGroupTitleAsync);
        group.MapGet("/threads/{threadId}/messages", GetMessagesAsync);
        group.MapPost("/threads/{threadId}/messages", PostMessageAsync);
        group.MapPost("/threads/{threadId}/messages/{messageId}/status", PostMessageStatusAsync);
        group.MapGet("/threads/{threadId}/route-sheets/has-unpaid", GetHasUnpaidRouteSheetsAsync);
        group.MapGet("/threads/{threadId}/route-sheets", GetRouteSheetsAsync);
        group.MapGet("/threads/{threadId}/route-sheets/{routeSheetId}/presel-preview", GetRouteSheetPreselPreviewAsync);
        group.MapPut("/threads/{threadId}/route-sheets/{routeSheetId}", PutRouteSheetAsync);
        group.MapPost("/threads/{threadId}/route-sheets/{routeSheetId}/notify-preselected", PostNotifyRouteSheetPreselectedAsync);
        group.MapPost("/threads/{threadId}/route-sheets/{routeSheetId}/duplicate", PostDuplicateRouteSheetAsync);
        group.MapDelete("/threads/{threadId}/route-sheets/{routeSheetId}", DeleteRouteSheetAsync);
        group.MapPost("/threads/{threadId}/route-sheets/{routeSheetId}/edit-carrier-response", PostRouteSheetEditCarrierResponseAsync);
        group.MapGet("/threads/{threadId}/route-tramo-subscriptions", GetRouteTramoSubscriptionsAsync);
        group.MapPost("/threads/{threadId}/route-tramo-subscriptions/accept", PostAcceptRouteTramoSubscriptionAsync);
        group.MapPost("/threads/{threadId}/route-tramo-subscriptions/reject", PostRejectRouteTramoSubscriptionAsync);
        group.MapPost("/threads/{threadId}/route-tramo-subscriptions/seller-expel-carrier", PostSellerExpelCarrierAsync);
        group.MapPost("/threads/{threadId}/route-sheet-presel-invite", PostCarrierRespondPreselInviteAsync);

        return app;
    }

    private static async Task<IResult> PostThreadAsync(
        CreateThreadBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var purchaseIntent = body.PurchaseIntent ?? true;
        var forceNew = body.ForceNew ?? false;
        var oid = body.OfferId ?? "";
        if (await chat.IsUserSellerForOfferAsync(userId, oid, cancellationToken))
        {
            return Results.BadRequest(new
            {
                error = "cannot_message_self",
                message = "No puedes chatear contigo mismo.",
            });
        }

        var dto = await chat.CreateOrGetThreadForBuyerAsync(userId, oid, purchaseIntent, forceNew, cancellationToken);
        if (dto is null)
            return Results.NotFound(new { error = "offer_not_found", message = "No se encontró la oferta o no puedes abrir este chat." });

        return Results.Ok(dto);
    }

    private static async Task<IResult> PostSocialGroupThreadAsync(
        CreateSocialGroupBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var ids = body.MemberUserIds ?? Array.Empty<string>();
        var dto = await chat.CreateSocialGroupThreadAsync(userId, ids, cancellationToken);
        if (dto is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_social_thread",
                message =
                    "No se pudo crear el chat. Necesitás al menos un contacto válido, una tienda asociada a tu cuenta, y no podés incluirte dos veces.",
            });
        }

        return Results.Ok(dto);
    }

    private static async Task<IResult> PostSupportThreadAsync(
        CreateSupportThreadBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var dto = await chat.CreateOrGetSupportThreadAsync(
            userId,
            body.StoreId ?? "",
            body.Motive ?? "",
            body.ReplyPhone ?? "",
            body.PublicNumber,
            cancellationToken);
        if (dto is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_support_thread",
                message = "No se pudo abrir el chat de soporte. Revisa el mensaje y el teléfono.",
            });
        }

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetThreadsAsync(
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await chat.ListThreadsForUserAsync(userId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> PostAckPendingDeliveryOnLoginAsync(
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var n = await chat.AckAllPendingIncomingDeliveredAsync(userId, cancellationToken);
        return Results.Ok(new AckPendingDeliveryOnLoginResult(n));
    }

    private static async Task<IResult> GetThreadByOfferAsync(
        string offerId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var dto = await chat.GetThreadByOfferIfVisibleAsync(userId, offerId, cancellationToken);
        if (dto is null)
            return Results.NotFound();
        return Results.Ok(dto);
    }

    private static async Task<IResult> PostNotifyParticipantLeftAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IBroadcastingService broadcasting,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var ok = await broadcasting.BroadcastParticipantLeftToOthersAsync(userId, threadId, cancellationToken);
        if (!ok)
            return Results.NotFound();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteThreadAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var ok = await chat.DeleteThreadAsync(userId, threadId, cancellationToken);
        if (!ok)
            return Results.NotFound(new { error = "not_found", message = "Hilo no encontrado o sin permiso." });
        return Results.NoContent();
    }

    private static async Task<IResult> GetThreadAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var dto = await chat.GetThreadIfVisibleAsync(userId, threadId, cancellationToken);
        if (dto is null)
            return Results.NotFound();
        return Results.Ok(dto);
    }

    private static async Task<IResult> GetSocialThreadMembersAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await chat.ListSocialThreadMembersAsync(userId, threadId, cancellationToken);
        if (list is null)
            return Results.NotFound();
        return Results.Ok(list);
    }

    private static async Task<IResult> PatchSocialGroupTitleAsync(
        string threadId,
        PatchSocialGroupTitleBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var dto = await chat.PatchSocialGroupTitleAsync(userId, threadId, body?.Title, cancellationToken);
        if (dto is null)
        {
            return Results.NotFound(new
            {
                error = "not_found",
                message = "No se pudo actualizar el nombre (sin permiso o chat no social).",
            });
        }

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetMessagesAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await chat.ListMessagesAsync(userId, threadId, cancellationToken);
        if (list.Count == 0)
        {
            var th = await chat.GetThreadIfVisibleAsync(userId, threadId, cancellationToken);
            if (th is null)
                return Results.NotFound();
        }
        return Results.Ok(list);
    }

    private static async Task<IResult> PostMessageAsync(
        string threadId,
        PostChatMessageBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var msg = await chat.PostMessageAsync(new PostChatMessageArgs(userId, threadId, body), cancellationToken);
        if (msg is null)
            return Results.NotFound(new { error = "not_found", message = "Hilo no encontrado o mensaje inválido." });
        return Results.Ok(msg);
    }

    private static async Task<IResult> PostMessageStatusAsync(
        string threadId,
        string messageId,
        UpdateMessageStatusBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (!Enum.TryParse<ChatMessageStatus>(body.Status, ignoreCase: true, out var st))
            return Results.BadRequest(new { error = "invalid_status" });
        var msg = await chat.UpdateMessageStatusAsync(
            new UpdateChatMessageStatusArgs(userId, threadId, messageId, st),
            cancellationToken);
        if (msg is null)
            return Results.NotFound(new { error = "not_found", message = "Mensaje o hilo no encontrado." });
        return Results.Ok(msg);
    }

    private static async Task<IResult> GetRouteSheetsAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await routeSheets.ListForThreadAsync(userId, threadId, cancellationToken);
        if (list is null)
            return Results.NotFound();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetHasUnpaidRouteSheetsAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        AppDbContext db,
        IChatService chat,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();

        var tid = (threadId ?? "").Trim();
        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (t is null || t.DeletedAtUtc is not null || !await chat.UserCanAccessThreadRowAsync(userId, t, cancellationToken))
            return Results.NotFound();

        var canCreateRouteSheet = await db.TradeAgreements.AsNoTracking()
            .Where(a => a.ThreadId == tid
                        && a.DeletedAtUtc == null
                        && a.Status == "accepted")
            .AnyAsync(
                a => !db.AgreementCurrencyPayments.AsNoTracking().Any(
                         p => p.TradeAgreementId == a.Id
                              && p.Status == AgreementPaymentStatuses.Succeeded)
                     || a.RouteSheetId == null
                     || a.RouteSheetId == "",
                cancellationToken);
        return Results.Ok(canCreateRouteSheet);
    }

    private static async Task<IResult> GetRouteSheetPreselPreviewAsync(
        string threadId,
        string routeSheetId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var payload = await routeSheets.GetPreselPreviewForCarrierAsync(
            userId,
            threadId,
            routeSheetId,
            cancellationToken);
        if (payload is null)
            return Results.NotFound();
        return Results.Ok(payload);
    }

    private static async Task<IResult> GetRouteTramoSubscriptionsAsync(
        string threadId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var list = await routeTramoSubscriptions.ListPublishedForThreadAsync(userId, threadId, cancellationToken);
        if (list is null)
            return Results.NotFound();
        return Results.Ok(list);
    }

    private static async Task<IResult> PostAcceptRouteTramoSubscriptionAsync(
        string threadId,
        AcceptRouteTramoSubscriptionBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.RouteSheetId)
            || string.IsNullOrWhiteSpace(body.CarrierUserId))
            return Results.BadRequest(new { error = "invalid_body" });

        try
        {
            var n = await routeTramoSubscriptions.AcceptCarrierPendingOnSheetAsync(
                new TramoSellerSheetAction(
                    userId,
                    threadId,
                    body.RouteSheetId.Trim(),
                    body.CarrierUserId.Trim(),
                    string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim()),
                cancellationToken);
            if (n is null)
                return Results.NotFound(new { error = "not_found", message = "No hay suscripciones que confirmar." });
            return Results.Ok(new { acceptedCount = n.Value });
        }
        catch (InvalidOperationException ex)
            when (ex.Message == RouteTramoSubscriptionPolicy.AcceptCarrierPendingConflictMessage)
        {
            return Results.Conflict(new { error = "tramo_already_confirmed", message = ex.Message });
        }
    }

    private static async Task<IResult> PostRejectRouteTramoSubscriptionAsync(
        string threadId,
        RejectRouteTramoSubscriptionBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.RouteSheetId)
            || string.IsNullOrWhiteSpace(body.CarrierUserId))
            return Results.BadRequest(new { error = "invalid_body" });

        var n = await routeTramoSubscriptions.RejectCarrierPendingOnSheetAsync(
            new TramoSellerSheetAction(
                userId,
                threadId,
                body.RouteSheetId.Trim(),
                body.CarrierUserId.Trim(),
                string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim()),
            cancellationToken);
        if (n is null)
            return Results.NotFound(new { error = "not_found", message = "No hay solicitudes pendientes que rechazar." });
        return Results.Ok(new { rejectedCount = n.Value });
    }

    private static async Task<IResult> PostSellerExpelCarrierAsync(
        string threadId,
        SellerExpelCarrierBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        IChatExitPolicyRegistry chatExitPolicyRegistry,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body is null
            || string.IsNullOrWhiteSpace(body.CarrierUserId)
            || string.IsNullOrWhiteSpace((body.Reason ?? "").Trim()))
            return Results.BadRequest(new { error = "invalid_body", message = "Indica al transportista y un motivo." });

        var rs = (body.RouteSheetId ?? "").Trim();
        var st = (body.StopId ?? "").Trim();
        if (rs.Length > 0 != st.Length > 0)
            return Results.BadRequest(new
            {
                error = "invalid_body",
                message = "Para expulsar solo un tramo envía routeSheetId y stopId; para toda la operación, ninguno de los dos.",
            });

        var r = await routeTramoSubscriptions.ExpelCarrierBySellerFromThreadAsync(
            userId,
            threadId,
            body.CarrierUserId.Trim(),
            (body.Reason ?? "").Trim(),
            rs.Length > 0 ? rs : null,
            st.Length > 0 ? st : null,
            cancellationToken);
        if (r is null)
            return Results.NotFound(new { error = "not_found", message = "No se pudo retirar al transportista (sin permiso o sin suscripciones activas)." });

        if (chatExitPolicyRegistry.TryMapSellerExpelFailure(r.ErrorCode, out var sellerStatus, out var sellerMessage))
        {
            return Results.Json(
                new { error = r.ErrorCode, message = sellerMessage },
                statusCode: sellerStatus);
        }

        return Results.Ok(r);
    }

    private static async Task<IResult> PutRouteSheetAsync(
        string threadId,
        string routeSheetId,
        RouteSheetPayload payload,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (RouteSheetUtils.ValidateEstimatedTimes(payload) is { } validationMessage)
            return Results.BadRequest(new { error = "validation", message = validationMessage });
        if (RouteSheetUtils.ValidateLinkedTramoChain(payload) is { } linkMessage)
            return Results.BadRequest(new { error = "tramos_not_linked", message = linkMessage });
        var result = await routeSheets.UpsertAsync(userId, threadId, routeSheetId, payload, cancellationToken);
        return result switch
        {
            RouteSheetMutationResult.Ok => Results.NoContent(),
            RouteSheetMutationResult.LockedByPaidAgreement => Results.Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar ni eliminar.",
            }),
            RouteSheetMutationResult.ExceedsUnpaidAgreementLimit => Results.Conflict(new
            {
                error = "exceeds_unpaid_agreement_limit",
                message = "Ya hay una hoja de ruta por cada acuerdo que puede vincularse. Vinculá las hojas existentes antes de crear una nueva.",
            }),
            RouteSheetMutationResult.RouteCurrencyMerchandiseMismatch => Results.BadRequest(new
            {
                error = "route_currency_merchandise_mismatch",
                message = AgreementCheckoutCurrency.RouteStopCurrencyMismatchMessage,
            }),
            RouteSheetMutationResult.CannotPublishDeliveredSheet => Results.Conflict(new
            {
                error = "cannot_publish_delivered_sheet",
                message = "Esta hoja de ruta ya fue entregada; no se puede publicar en la plataforma.",
            }),
            RouteSheetMutationResult.CannotPublishWithoutAgreementLink => Results.BadRequest(new
            {
                error = "route_sheet_not_linked",
                message = "Vinculá la hoja de ruta a un acuerdo antes de publicarla en la plataforma.",
            }),
            _ => Results.NotFound(new { error = "not_found", message = "Hilo no encontrado o datos inválidos." }),
        };
    }

    private static async Task<IResult> PostNotifyRouteSheetPreselectedAsync(
        string threadId,
        string routeSheetId,
        NotifyPreselectedBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body?.Invites is null || body.Invites.Length == 0)
            return Results.BadRequest(new { error = "invalid_body", message = "Indica al menos un tramo con teléfono a notificar." });
        if (await routeSheets.RouteSheetIsLockedByPaidAgreementAsync(threadId, routeSheetId, cancellationToken))
        {
            var confirmedStopIds = await routeSheets.LoadConfirmedRouteStopIdsAsync(
                threadId,
                routeSheetId,
                cancellationToken);
            if (!RouteSheetPaidEditPolicy.InvitesTargetOnlyVacantStops(body.Invites, confirmedStopIds))
                return Results.Conflict(new
                {
                    error = "locked_paid_agreement",
                    message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar ni eliminar.",
                });
        }
        var n = await routeSheets.NotifyPreselectedTransportistasAsync(
            userId,
            threadId,
            routeSheetId,
            body.Invites,
            cancellationToken);
        if (n < 0)
            return Results.NotFound(new { error = "not_found", message = "Hilo o hoja no encontrados, o sin permiso." });
        return Results.Ok(new NotifyPreselectedResult(n));
    }

    private static async Task<IResult> PostCarrierRespondPreselInviteAsync(
        string threadId,
        CarrierPreselInviteBody? body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteTramoSubscriptionService routeTramoSubscriptions,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.RouteSheetId))
            return Results.BadRequest(new { error = "invalid_body" });
        var stop = string.IsNullOrWhiteSpace(body.StopId) ? null : body.StopId.Trim();
        var rsid = body.RouteSheetId.Trim();
        var ok = await routeTramoSubscriptions.CarrierRespondPreselectedRouteInviteAsync(
            new CarrierPreselInviteRequest(userId, threadId, rsid, stop, body.Accepted),
            cancellationToken);
        var errMsg = body.Accepted
            ? "No se pudo confirmar (teléfono en hoja, hilo o permisos)."
            : "No se pudo registrar el rechazo.";
        if (!ok)
            return Results.NotFound(new { error = "not_found", message = errMsg });
        return Results.Ok(new { ok = true, accepted = body.Accepted });
    }

    private static async Task<IResult> PostDuplicateRouteSheetAsync(
        string threadId,
        string routeSheetId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var (payload, err) = await routeSheets.DuplicateAsync(
            userId,
            threadId,
            routeSheetId,
            cancellationToken);
        if (payload is not null)
            return Results.Ok(payload);
        return err switch
        {
            RouteSheetMutationResult.LockedByPaidAgreement => Results.Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede duplicar.",
            }),
            RouteSheetMutationResult.ExceedsUnpaidAgreementLimit => Results.Conflict(new
            {
                error = "exceeds_unpaid_agreement_limit",
                message = "Ya hay una hoja de ruta por cada acuerdo que puede vincularse. Vinculá las hojas existentes antes de crear una nueva.",
            }),
            _ => Results.NotFound(new { error = "not_found", message = "Hoja no encontrada o sin permiso." }),
        };
    }

    private static async Task<IResult> DeleteRouteSheetAsync(
        string threadId,
        string routeSheetId,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        var result = await routeSheets.DeleteAsync(userId, threadId, routeSheetId, cancellationToken);
        return result switch
        {
            RouteSheetMutationResult.Ok => Results.NoContent(),
            RouteSheetMutationResult.LockedByPaidAgreement => Results.Conflict(new
            {
                error = "locked_paid_agreement",
                message = "Esta hoja está vinculada a un acuerdo con cobros registrados; no se puede editar ni eliminar.",
            }),
            _ => Results.NotFound(new { error = "not_found", message = "Hoja no encontrada o sin permiso." }),
        };
    }

    private static async Task<IResult> PostRouteSheetEditCarrierResponseAsync(
        string threadId,
        string routeSheetId,
        RouteSheetEditCarrierResponseBody body,
        HttpRequest request,
        ICurrentUserAccessor currentUser,
        IRouteSheetChatService routeSheets,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId(request);
        if (userId is null)
            return Results.Unauthorized();
        if (body is null)
            return Results.BadRequest(new { error = "invalid_body" });
        var ok = await routeSheets.CarrierRespondToSheetEditAsync(
            userId,
            threadId,
            routeSheetId,
            body.Accept,
            cancellationToken);
        if (!ok)
            return Results.NotFound(new { error = "not_found", message = "Sin permiso, sin acuse pendiente o hoja inexistente." });
        return Results.NoContent();
    }
}
