using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Chat.Dtos;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Core;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Popularity;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.EmergentOffers;

public sealed class EmergentRouteTramoSubscriptionRequestService(
    AppDbContext db,
    INotificationService notifications,
    IBroadcastingService broadcasting,
    IRouteTramoSubscriptionService routeTramoSubscriptions) : IEmergentRouteTramoSubscriptionRequestService
{
    public const string ErrInvalidEmergent = "invalid_emergent_offer";
    public const string ErrInvalidStop = "invalid_stop";
    public const string ErrNotPublished = "route_not_published";
    public const string ErrInvalidService = "invalid_transport_service";
    public const string ErrServiceNotTransport = "service_not_transport";

    public async Task<(bool Ok, string? ErrorCode, string? Message)> RequestAsync(
        string carrierUserId,
        string emergentOfferId,
        string stopId,
        string storeServiceId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        if (uid.Length < 2)
            return (false, "unauthorized", "Sesión requerida.");

        var eid = (emergentOfferId ?? "").Trim();
        if (eid.Length < 4 || !RecommendationBatchOfferLoader.IsEmergentPublicationId(eid))
            return (false, ErrInvalidEmergent, "Publicación emergente no válida.");

        var sid = (stopId ?? "").Trim();
        var svcId = (storeServiceId ?? "").Trim();
        if (sid.Length < 1 || svcId.Length < 1)
            return (false, "invalid_payload", "Indica tramo y servicio de transporte.");

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid && x.RetractedAtUtc == null, cancellationToken);
        if (em is null)
            return (false, ErrInvalidEmergent, "La publicación no está activa.");

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == em.ThreadId && x.DeletedAtUtc == null,
                cancellationToken);
        if (thread is null)
            return (false, ErrInvalidEmergent, "Hilo no encontrado.");

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == em.ThreadId
                    && x.RouteSheetId == em.RouteSheetId
                    && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return (false, ErrNotPublished, "La hoja de ruta no está publicada en la plataforma.");

        var payload = sheetRow.Payload;
        if (payload.PublicadaPlataforma != true)
            return (false, ErrNotPublished, "La hoja de ruta no está publicada.");

        var paradas = payload.Paradas ?? [];
        var stop = paradas.FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
        if (stop is null)
            return (false, ErrInvalidStop, "El tramo no pertenece a esta hoja.");

        var service = await db.StoreServices
            .AsNoTracking()
            .Include(s => s.Store)
            .FirstOrDefaultAsync(x => x.Id == svcId, cancellationToken);
        if (service is null || service.DeletedAtUtc is not null)
            return (false, ErrInvalidService, "Servicio no encontrado.");

        var owner = (service.Store?.OwnerUserId ?? "").Trim();
        if (!string.Equals(owner, uid, StringComparison.Ordinal))
            return (false, ErrInvalidService, "Ese servicio no pertenece a tus tiendas.");

        if (!TransportServiceQualification.ServiceQualifiesAsTransport(service))
            return (false, ErrServiceNotTransport, "El servicio elegido no califica como transporte o logística publicada.");

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

        var phoneSnap = (carrierAccount?.PhoneDisplay ?? "").Trim();
        if (phoneSnap.Length == 0 && !string.IsNullOrWhiteSpace(carrierAccount?.PhoneDigits))
            phoneSnap = carrierAccount!.PhoneDigits!.Trim();
        if (phoneSnap.Length > 40)
            phoneSnap = phoneSnap[..40];

        await routeTramoSubscriptions.RecordSubscriptionRequestAsync(
            new RecordRouteTramoSubscriptionRequestArgs(
                em.ThreadId,
                em.RouteSheetId,
                sid,
                orden,
                uid,
                svcId,
                svcLabel,
                phoneSnap.Length > 0 ? phoneSnap : null),
            cancellationToken);

        var meta = JsonSerializer.Serialize(
            new RouteTramoSubscribeMeta(em.RouteSheetId, sid, uid),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

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

        // Solo vendedor (no comprador): nueva solicitud de tramo.
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

        // Suscriptor del tramo: constancia de envío (mismo hilo / meta para deep link).
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

        return (true, null, null);
    }

    private sealed record RouteTramoSubscribeMeta(string RouteSheetId, string StopId, string CarrierUserId);
}
