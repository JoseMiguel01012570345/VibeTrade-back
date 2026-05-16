using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;


using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierTelemetryService(
    AppDbContext db,
    IChatService chat,
    INotificationService notifications,
    IBroadcastingService broadcasting) : ICarrierTelemetryService
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

        var hasDelivery = await db.RouteStopDeliveries.AsNoTracking().AnyAsync(
            x =>
                x.ThreadId == tid
                && x.TradeAgreementId == aid
                && x.RouteSheetId == rsid
                && x.RouteStopId == sid
                && x.State != RouteStopDeliveryStates.Unpaid
                && x.State != RouteStopDeliveryStates.EvidenceAccepted
                && x.State != RouteStopDeliveryStates.IdleStoreCustody
                && x.State != RouteStopDeliveryStates.Refunded
                && x.State != RouteStopDeliveryStates.EvidenceSubmitExpired,
            cancellationToken)
            .ConfigureAwait(false);
        if (!hasDelivery)
            return new CarrierTelemetryIngestResultDto(
                false,
                "delivery_not_found", "Este tramo no tiene un paquete activo.",
                null,
                true,
                null,
                null
            );

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (thread is null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken).ConfigureAwait(false))
            return null;

        var avatarUrl = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => u.AvatarUrl)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

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
            return new CarrierTelemetryIngestResultDto(false, "not_confirmed_carrier", "No eres transportista confirmado en este tramo.", null,
                true, null, avatarUrl);

        var sheet = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (sheet?.Payload.Paradas is not { Count: > 0 } stops)
            return new CarrierTelemetryIngestResultDto(false, "route_sheet_not_found", "No se encontró la hoja de ruta.", null,
                true, null, avatarUrl);

        var stop = stops.FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
        if (stop is null)
            return new CarrierTelemetryIngestResultDto(false, "stop_not_found", "Tramo inválido.", null, true, null, avatarUrl);

        List<List<double>> poly = stop.OsrmRouteLatLngs ?? [];
        if (poly.Count < 2)
        {
            // Fallback recto O→D si no hay OSRM persistido.
            if (LogisticsUtils.TryParseLatLng(stop.OrigenLat, stop.OrigenLng, out var oLat, out var oLng)
                && LogisticsUtils.TryParseLatLng(stop.DestinoLat, stop.DestinoLng, out var dLat, out var dLng))
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
                null, true, null, avatarUrl);

        var routeLen = PolylineLengthMeters(poly);
        var tol = AdaptiveToleranceMeters(routeLen);
        var projection = ProjectToPolyline(lat, lng, poly, tol);

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
            return new CarrierTelemetryIngestResultDto(true, null, null, last.ProgressFraction, last.OffRoute, last.SpeedKmh,
                avatarUrl);

        var resolvedSpeedKmh = ResolveSpeedKmh(last, lat, lng, reportedAtUtc, speedKmh);

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
            SpeedKmh = resolvedSpeedKmh,
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
                projection.OffRoute, resolvedSpeedKmh, avatarUrl);
        }

        if (!string.Equals(delivery.CurrentOwnerUserId, uid, StringComparison.Ordinal))
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CarrierTelemetryIngestResultDto(false, "not_owner", "No tienes el paquete en este tramo.", null,
                projection.OffRoute, resolvedSpeedKmh, avatarUrl);
        }

        if (delivery.State is RouteStopDeliveryStates.Paid or RouteStopDeliveryStates.AwaitingCarrierForHandoff)
            delivery.State = RouteStopDeliveryStates.InTransit;

        delivery.LastTelemetryProgressFraction = projection.Progress01;
        delivery.UpdatedAtUtc = now;

        await TryProximityNotifyAsync(tid, aid, rsid, stops, sid, uid, projection.Progress01, delivery, cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await broadcasting.BroadcastCarrierTelemetryUpdatedAsync(
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
                resolvedSpeedKmh,
                avatarUrl,
                cancellationToken)
            .ConfigureAwait(false);

        return new CarrierTelemetryIngestResultDto(true, null, null, projection.Progress01, projection.OffRoute, resolvedSpeedKmh,
            avatarUrl);
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
            .Where(x => x.ThreadId == tid && x.TradeAgreementId == aid && x.RouteSheetId == rsid && x.State != RouteStopDeliveryStates.EvidenceAccepted)
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

        var ownerIds = deliveries
            .Select(x => (x.CurrentOwnerUserId ?? "").Trim())
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var avatarByUser = await db.UserAccounts.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarUrl })
            .ToDictionaryAsync(x => x.Id, x => x.AvatarUrl, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<CarrierTelemetryLatestPointDto>();
        foreach (var d in deliveries)
        {
            var owner = (d.CurrentOwnerUserId ?? "").Trim();
            var sid = (d.RouteStopId ?? "").Trim();
            if (owner.Length < 2 || sid.Length < 1)
                continue;
            if (!latestByStopAndCarrier.TryGetValue((sid, owner), out var sample))
                continue;

            avatarByUser.TryGetValue(owner, out var av);

            rows.Add(new CarrierTelemetryLatestPointDto(
                rsid,
                sid,
                owner,
                sample.Lat,
                sample.Lng,
                sample.ProgressFraction,
                sample.OffRoute,
                sample.ReportedAtUtc,
                sample.SpeedKmh,
                av));
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

        var preview =
            "El transportista anterior está cerca del fin de tramo: podés coordinar el handoff cuando corresponda.";

        await notifications.NotifyRouteLegProximityAsync(
                new RouteLegProximityNotificationArgs(
                    nc,
                    threadId,
                    routeSheetId,
                    agreementId,
                    currentStopId,
                    preview),
                cancellationToken)
            .ConfigureAwait(false);

        deliveryRow.ProximityNotifiedAtUtc = DateTimeOffset.UtcNow;
    }

    private static double? ResolveSpeedKmh(
        CarrierTelemetrySampleRow? prev,
        double lat,
        double lng,
        DateTimeOffset reportedAtUtc,
        double? clientSpeedKmh)
    {
        if (prev is null)
            return clientSpeedKmh;
        var dt = reportedAtUtc - prev.ReportedAtUtc;
        if (dt <= TimeSpan.Zero || dt.TotalSeconds < 0.5)
            return clientSpeedKmh;
        var distM = LogisticsUtils.HaversineMeters(prev.Lat, prev.Lng, lat, lng);
        var hours = dt.TotalHours;
        if (hours <= 0)
            return clientSpeedKmh;
        return distM / 1000.0 / hours;
    }

    private static double PolylineLengthMeters(IReadOnlyList<List<double>> poly)
    {
        double sum = 0;
        for (var i = 1; i < poly.Count; i++)
        {
            var a = poly[i - 1];
            var b = poly[i];
            if (a.Count < 2 || b.Count < 2) continue;
            sum += LogisticsUtils.HaversineMeters(a[0], a[1], b[0], b[1]);
        }

        return sum;
    }

    private readonly record struct ProjectionResult(
        double DistanceToPolylineMeters,
        double DistanceAlongMeters,
        double TotalLengthMeters,
        double Progress01,
        bool OffRoute);

    private static ProjectionResult ProjectToPolyline(
        double lat,
        double lng,
        IReadOnlyList<List<double>> latLngPoints,
        double offRouteToleranceMeters)
    {
        if (latLngPoints.Count < 2)
            return new ProjectionResult(double.NaN, 0, 0, 0, true);

        var pts = new List<(double Lat, double Lng)>(latLngPoints.Count);
        foreach (var p in latLngPoints)
        {
            if (p.Count < 2) continue;
            pts.Add((p[0], p[1]));
        }

        if (pts.Count < 2)
            return new ProjectionResult(double.NaN, 0, 0, 0, true);

        double bestDist = double.PositiveInfinity;
        var cumulative = new double[pts.Count];
        cumulative[0] = 0;
        for (var i = 1; i < pts.Count; i++)
        {
            cumulative[i] = cumulative[i - 1]
                + LogisticsUtils.HaversineMeters(pts[i - 1].Lat, pts[i - 1].Lng, pts[i].Lat, pts[i].Lng);
        }

        var total = cumulative[^1];
        if (total <= 1e-6)
            return new ProjectionResult(0, 0, 0, 0, false);

        double alongBest = 0;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var segLen = cumulative[i] - cumulative[i - 1];
            if (segLen <= 1e-9)
                continue;

            var (d, t) = LogisticsUtils.DistancePointToSegmentMeters(lat, lng, a, b);
            if (d < bestDist)
            {
                bestDist = d;
                alongBest = cumulative[i - 1] + t * segLen;
            }
        }

        var progress = LogisticsUtils.Clamp01(alongBest / total);
        var off = bestDist > offRouteToleranceMeters;
        return new ProjectionResult(bestDist, alongBest, total, progress, off);
    }

    private static double AdaptiveToleranceMeters(double totalRouteMeters)
    {
        if (double.IsNaN(totalRouteMeters) || totalRouteMeters <= 0)
            return 45;
        var baseTol = 25 + Math.Sqrt(totalRouteMeters) * 0.35;
        return LogisticsUtils.Clamp(baseTol, 35, 220);
    }
}
