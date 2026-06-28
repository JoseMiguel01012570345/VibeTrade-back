using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.RouteTramoSubscriptions;

namespace VibeTrade.Backend.Features.EmergentOffers.EmergentOffersMediator.RequestRouteTramoSubscription;

public sealed class RequestRouteTramoSubscriptionHandler(
    AppDbContext db,
    INotificationService notifications,
    IBroadcastingService broadcasting,
    IRouteTramoSubscriptionService routeTramoSubscriptions)
    : IRequestHandler<RequestRouteTramoSubscriptionCommand, RequestRouteTramoSubscriptionResult>
{
    public async Task<RequestRouteTramoSubscriptionResult> Handle(
        RequestRouteTramoSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var uid = (request.CarrierUserId ?? "").Trim();
        if (uid.Length < 2)
            return new RequestRouteTramoSubscriptionResult(false, "unauthorized", "Sesión requerida.");

        if (!EmergentOfferUtils.TryNormalizeEmergentOfferId(request.EmergentOfferId, out var eid))
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidEmergent,
                "Publicación emergente no válida.");

        var sid = (request.StopId ?? "").Trim();
        var svcId = (request.StoreServiceId ?? "").Trim();
        if (sid.Length < 1 || svcId.Length < 1)
            return new RequestRouteTramoSubscriptionResult(false, "invalid_payload", "Indica tramo y servicio de transporte.");

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid && x.RetractedAtUtc == null, cancellationToken);
        if (em is null)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidEmergent,
                "La publicación no está activa.");

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == em.ThreadId && x.DeletedAtUtc == null,
                cancellationToken);
        if (thread is null)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidEmergent,
                "Hilo no encontrado.");

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == em.ThreadId
                    && x.RouteSheetId == em.RouteSheetId
                    && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrNotPublished,
                "La hoja de ruta no está publicada en la plataforma.");

        var payload = sheetRow.Payload;
        if (payload.PublicadaPlataforma != true)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrNotPublished,
                "La hoja de ruta no está publicada.");

        var paradas = payload.Paradas ?? [];
        var stop = paradas.FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
        if (stop is null)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidStop,
                "El tramo no pertenece a esta hoja.");

        if (SubscriptionsUtils.RouteSheetPayloadIsDelivered(payload))
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrNotPublished,
                "La hoja de ruta no está publicada en la plataforma.");

        var stopDeliveryState = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == em.ThreadId
                && x.RouteSheetId == em.RouteSheetId
                && x.RouteStopId == sid)
            .Select(x => x.State)
            .FirstOrDefaultAsync(cancellationToken);
        if (SubscriptionsUtils.StopDeliveryIsEvidenceAccepted(stopDeliveryState))
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrStopDelivered,
                RouteTramoSubscriptionService.StopDeliveredSubscriptionBlockedMessage);

        var service = await db.StoreServices
            .AsNoTracking()
            .Include(s => s.Store)
            .FirstOrDefaultAsync(x => x.Id == svcId, cancellationToken);
        if (service is null || service.DeletedAtUtc is not null)
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidService,
                "Servicio no encontrado.");

        var owner = (service.Store?.OwnerUserId ?? "").Trim();
        if (!string.Equals(owner, uid, StringComparison.Ordinal))
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrInvalidService,
                "Ese servicio no pertenece a tus tiendas.");

        if (!TransportServiceQualification.ServiceQualifiesAsTransport(service))
            return new RequestRouteTramoSubscriptionResult(
                false,
                EmergentRouteTramoSubscriptionRequestService.ErrServiceNotTransport,
                "El servicio elegido no califica como transporte o logística publicada.");

        var carrierAccount = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == uid, cancellationToken);
        var authorLabel = string.IsNullOrWhiteSpace(carrierAccount?.DisplayName)
            ? "Transportista"
            : carrierAccount!.DisplayName.Trim();
        var trust = carrierAccount?.TrustScore ?? 0;

        var tipo = (service.TipoServicio ?? "").Trim();
        var cat = (service.Category ?? "").Trim();
        var svcLabel = string.Join(" · ", new[] { tipo, cat }.Where(x => x.Length > 0));
        if (svcLabel.Length == 0)
            svcLabel = "Servicio de transporte";

        var orden = stop.Orden;
        var preview =
            $"{authorLabel} solicitó el tramo {orden} con el servicio «{svcLabel}». Pendiente de validación.";

        var phoneSnap = EmergentOfferUtils.NormalizePhoneSnapshot(
            carrierAccount?.PhoneDisplay,
            carrierAccount?.PhoneDigits);

        await routeTramoSubscriptions.RecordSubscriptionRequestAsync(
            new RecordRouteTramoSubscriptionRequestArgs(
                em.ThreadId,
                em.RouteSheetId,
                sid,
                orden,
                uid,
                svcId,
                svcLabel,
                phoneSnap is { Length: > 0 } snap ? snap : null),
            cancellationToken);

        var meta = EmergentOfferUtils.BuildRouteTramoSubscribeMetaJson(em.RouteSheetId, sid, uid);

        var sellerId = (thread.SellerUserId ?? "").Trim();
        var storeRow = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
        var sellerOpLabel = string.IsNullOrWhiteSpace(storeRow?.Name)
            ? "El vendedor"
            : storeRow!.Name.Trim();
        var sellerTrustBadge = storeRow?.TrustScore ?? 0;

        await broadcasting.BroadcastRouteTramoSubscriptionsChangedAsync(
            new RouteTramoSubscriptionsBroadcastArgs(em.ThreadId, em.RouteSheetId, "request", uid, eid),
            cancellationToken);

        if (sellerId.Length > 0 && !string.Equals(sellerId, uid, StringComparison.Ordinal))
        {
            await notifications.NotifyRouteTramoSubscriptionRequestAsync(
                new RouteTramoSubscriptionRequestNotificationArgs(
                    new List<string> { sellerId },
                    thread.Id,
                    preview,
                    authorLabel,
                    trust,
                    uid,
                    meta),
                cancellationToken);
        }

        var carrierAck =
            $"Tu solicitud del tramo {orden} quedó registrada. {sellerOpLabel} puede confirmarla desde el chat de esta operación.";
        await notifications.NotifyRouteTramoSubscriptionRequestAsync(
            new RouteTramoSubscriptionRequestNotificationArgs(
                new List<string> { uid },
                thread.Id,
                carrierAck,
                sellerOpLabel,
                sellerTrustBadge,
                uid,
                meta),
            cancellationToken);

        return new RequestRouteTramoSubscriptionResult(true, null, null);
    }
}
