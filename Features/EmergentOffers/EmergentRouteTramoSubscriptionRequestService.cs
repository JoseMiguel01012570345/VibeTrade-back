using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.EmergentOffers;

public sealed class EmergentRouteTramoSubscriptionRequestService(
    AppDbContext db,
    IChatService chat) : IEmergentRouteTramoSubscriptionRequestService
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
            return (false, "invalid_payload", "Indicá tramo y servicio de transporte.");

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

        var meta = JsonSerializer.Serialize(
            new RouteTramoSubscribeMeta(em.RouteSheetId, sid, uid),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var recipients = new HashSet<string>(StringComparer.Ordinal)
        {
            (thread.BuyerUserId ?? "").Trim(),
            (thread.SellerUserId ?? "").Trim(),
        };
        recipients.Remove("");
        recipients.Remove(uid);

        if (recipients.Count == 0)
            return (true, null, null);

        await chat.NotifyRouteTramoSubscriptionRequestAsync(
            recipients.ToList(),
            thread.Id,
            preview,
            authorLabel,
            trust,
            uid,
            meta,
            cancellationToken);

        return (true, null, null);
    }

    private sealed record RouteTramoSubscribeMeta(string RouteSheetId, string StopId, string CarrierUserId);
}
