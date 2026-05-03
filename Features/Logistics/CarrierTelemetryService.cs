using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Chat;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierTelemetryService(AppDbContext db, IChatService chat) : ICarrierTelemetryService
{
    private const double ProximityThreshold = 0.80;

    public async Task<CarrierTelemetryIngestResultDto?> IngestAsync(
        string actorUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        double lat,
        double lng,
        double? speedKmh,
        DateTimeOffset reportedAtUtc,
        string sourceClientId,
        CancellationToken cancellationToken = default)
    {
        var uid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        var client = (sourceClientId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1 || client.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var subOk = await db.RouteTramoSubscriptions.AsNoTracking().AnyAsync(
                x =>
                    x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);
        if (!subOk)
            return new CarrierTelemetryIngestResultDto(false, "not_confirmed_carrier", "No sos el transportista confirmado en este tramo.", null,
                true);

        var sheet = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (sheet?.Payload.Paradas is not { Count: > 0 } stops)
            return new CarrierTelemetryIngestResultDto(false, "route_sheet_not_found", "No se encontró la hoja de ruta.", null,
                true);

        var stop = stops.FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
        if (stop is null)
            return new CarrierTelemetryIngestResultDto(false, "stop_not_found", "Tramo inválido.", null, true);

        List<List<double>> poly = stop.OsrmRouteLatLngs ?? [];
        if (poly.Count < 2)
        {
            // Fallback recto O→D si no hay OSRM persistido.
            if (TryParseLatLng(stop.OrigenLat, stop.OrigenLng, out var oLat, out var oLng)
                && TryParseLatLng(stop.DestinoLat, stop.DestinoLng, out var dLat, out var dLng))
            {
                poly =
                [
                    new List<double> { oLat, oLng },
                    new List<double> { dLat, dLng },
                ];
            }
        }

        if (poly.Count < 2)
            return new CarrierTelemetryIngestResultDto(false, "no_geometry", "Este tramo no tiene geometría para proyectar GPS.",
                null, true);

        var routeLen = PolylineLengthMeters(poly);
        var tol = PolylineProjection.AdaptiveToleranceMeters(routeLen);
        var projection = PolylineProjection.ProjectToPolyline(lat, lng, poly, tol);

        var last = await db.CarrierTelemetrySamples.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.RouteStopId == sid
                && x.CarrierUserId == uid)
            .OrderByDescending(x => x.ReportedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (last is not null && last.ReportedAtUtc > reportedAtUtc)
            return new CarrierTelemetryIngestResultDto(true, null, null, last.ProgressFraction, last.OffRoute);

        var now = DateTimeOffset.UtcNow;
        var row = new CarrierTelemetrySampleRow
        {
            Id = "cts_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = sid,
            CarrierUserId = uid,
            Lat = lat,
            Lng = lng,
            SpeedKmh = speedKmh,
            ReportedAtUtc = reportedAtUtc,
            ServerReceivedAtUtc = now,
            SourceClientId = client,
            ProgressFraction = projection.Progress01,
            OffRoute = projection.OffRoute,
        };
        db.CarrierTelemetrySamples.Add(row);

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CarrierTelemetryIngestResultDto(false, "delivery_missing", "Este tramo no está activo en el acuerdo.", null,
                projection.OffRoute);
        }

        if (!string.Equals(delivery.CurrentOwnerUserId, uid, StringComparison.Ordinal))
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CarrierTelemetryIngestResultDto(false, "not_owner", "No tenés el paquete en este tramo (ownership).", null,
                projection.OffRoute);
        }

        if (delivery.State is RouteStopDeliveryStates.Paid or RouteStopDeliveryStates.AwaitingCarrierForHandoff)
            delivery.State = RouteStopDeliveryStates.InTransit;

        delivery.LastTelemetryProgressFraction = projection.Progress01;
        delivery.UpdatedAtUtc = now;

        if (projection.Progress01 >= 0.995
            && delivery.State is RouteStopDeliveryStates.InTransit or RouteStopDeliveryStates.Paid)
        {
            delivery.State = RouteStopDeliveryStates.DeliveredPendingEvidence;
            delivery.EvidenceDeadlineAtUtc ??= now.AddHours(24);
        }

        await TryProximityNotifyAsync(tid, aid, rsid, stops, sid, uid, projection.Progress01, delivery, cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await chat.BroadcastCarrierTelemetryUpdatedAsync(
                tid,
                rsid,
                aid,
                sid,
                uid,
                lat,
                lng,
                projection.Progress01,
                projection.OffRoute,
                reportedAtUtc,
                speedKmh,
                cancellationToken)
            .ConfigureAwait(false);

        return new CarrierTelemetryIngestResultDto(true, null, null, projection.Progress01, projection.OffRoute);
    }

    public async Task<IReadOnlyList<RouteStopDeliveryStatusDto>?> ListDeliveriesAsync(
        string viewerUserId,
        string threadId,
        string agreementId,
        CancellationToken cancellationToken = default)
    {
        var uid = (viewerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var rows = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.TradeAgreementId == aid)
            .OrderBy(x => x.RouteSheetId).ThenBy(x => x.RouteStopId)
            .Select(x => new RouteStopDeliveryStatusDto(
                x.RouteSheetId,
                x.RouteStopId,
                x.State,
                x.CurrentOwnerUserId,
                x.LastTelemetryProgressFraction,
                x.ProximityNotifiedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    public async Task<IReadOnlyList<CarrierTelemetryLatestPointDto>?> ListLatestTelemetryForRouteSheetAsync(
        string viewerUserId,
        string threadId,
        string agreementId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var uid = (viewerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var deliveries = await db.RouteStopDeliveries.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.TradeAgreementId == aid && x.RouteSheetId == rsid)
            .Select(x => new { x.RouteStopId, x.CurrentOwnerUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var samples = await db.CarrierTelemetrySamples.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latestByStopAndCarrier = samples
            .GroupBy(s => (
                Stop: (s.RouteStopId ?? "").Trim(),
                Carrier: (s.CarrierUserId ?? "").Trim()))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.ReportedAtUtc).First());

        var rows = new List<CarrierTelemetryLatestPointDto>();
        foreach (var d in deliveries)
        {
            var owner = (d.CurrentOwnerUserId ?? "").Trim();
            var sid = (d.RouteStopId ?? "").Trim();
            if (owner.Length < 2 || sid.Length < 1)
                continue;
            if (!latestByStopAndCarrier.TryGetValue((sid, owner), out var sample))
                continue;

            rows.Add(new CarrierTelemetryLatestPointDto(
                rsid,
                sid,
                owner,
                sample.Lat,
                sample.Lng,
                sample.ProgressFraction,
                sample.OffRoute,
                sample.ReportedAtUtc,
                sample.SpeedKmh));
        }

        return rows;
    }

    private async Task TryProximityNotifyAsync(
        string threadId,
        string agreementId,
        string routeSheetId,
        List<RouteStopPayload> stopsOrdered,
        string currentStopId,
        string currentCarrierUserId,
        double progress01,
        RouteStopDeliveryRow deliveryRow,
        CancellationToken cancellationToken)
    {
        if (progress01 + 1e-6 < ProximityThreshold)
            return;
        if (deliveryRow.ProximityNotifiedAtUtc is not null)
            return;

        var ordered = stopsOrdered.OrderBy(x => x.Orden).ToList();
        var idx = ordered.FindIndex(p => string.Equals((p.Id ?? "").Trim(), currentStopId, StringComparison.Ordinal));
        if (idx < 0 || idx >= ordered.Count - 1)
            return;

        var nextStopId = (ordered[idx + 1].Id ?? "").Trim();
        if (nextStopId.Length == 0)
            return;

        var nextCarrier = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == threadId
                && x.RouteSheetId == routeSheetId
                && x.StopId == nextStopId
                && x.Status == "confirmed")
            .Select(x => x.CarrierUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var nc = (nextCarrier ?? "").Trim();
        if (nc.Length < 2)
            return;

        if (string.Equals(nc, currentCarrierUserId, StringComparison.Ordinal))
            return;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return;

        var buyer = (thread.BuyerUserId ?? "").Trim();
        var seller = (thread.SellerUserId ?? "").Trim();
        var preview =
            "El transportista está cerca del punto de handoff del tramo. Coordiná la recepción con el siguiente transportista.";

        foreach (var rid in new[] { buyer, seller }.Where(x => x.Length >= 2).Distinct(StringComparer.Ordinal))
        {
            await chat.NotifyRouteLegProximityAsync(
                    new RouteLegProximityNotificationArgs(
                        rid,
                        threadId,
                        routeSheetId,
                        agreementId,
                        currentStopId,
                        preview),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        deliveryRow.ProximityNotifiedAtUtc = DateTimeOffset.UtcNow;
    }

    private static double PolylineLengthMeters(IReadOnlyList<List<double>> poly)
    {
        double sum = 0;
        for (var i = 1; i < poly.Count; i++)
        {
            var a = poly[i - 1];
            var b = poly[i];
            if (a.Count < 2 || b.Count < 2) continue;
            sum += HaversineMeters(a[0], a[1], b[0], b[1]);
        }

        return sum;
    }

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lng2 - lng1) * Math.PI / 180;
        var rLat1 = lat1 * Math.PI / 180;
        var rLat2 = lat2 * Math.PI / 180;
        var h =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(rLat1) * Math.Cos(rLat2) * (Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return R * c;
    }

    private static bool TryParseLatLng(string? latRaw, string? lngRaw, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var lt = (latRaw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal);
        var lg = (lngRaw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(lt, System.Globalization.CultureInfo.InvariantCulture, out lat)
               && double.TryParse(lg, System.Globalization.CultureInfo.InvariantCulture, out lng);
    }
}
