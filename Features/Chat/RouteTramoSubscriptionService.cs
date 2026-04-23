using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteTramoSubscriptionService(AppDbContext db, IChatService chat) : IRouteTramoSubscriptionService
{
    public async Task RecordSubscriptionRequestAsync(
        string threadId,
        string routeSheetId,
        string stopId,
        int stopOrden,
        string carrierUserId,
        string? storeServiceId,
        string transportServiceLabel,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (stopId ?? "").Trim();
        var uid = (carrierUserId ?? "").Trim();
        if (tid.Length < 2 || rsid.Length < 1 || sid.Length < 1 || uid.Length < 2)
            return;

        var label = (transportServiceLabel ?? "").Trim();
        if (label.Length > 512)
            label = label[..512];

        var svcTrim = string.IsNullOrWhiteSpace(storeServiceId) ? null : storeServiceId.Trim();
        if (svcTrim is { Length: > 64 })
            svcTrim = svcTrim[..64];

        var existing = await db.RouteTramoSubscriptions
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.StopOrden = stopOrden;
            existing.StoreServiceId = svcTrim;
            existing.TransportServiceLabel = label.Length > 0 ? label : existing.TransportServiceLabel;
            existing.Status = "pending";
            existing.UpdatedAtUtc = now;
        }
        else
        {
            db.RouteTramoSubscriptions.Add(new RouteTramoSubscriptionRow
            {
                Id = "rts_" + Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                RouteSheetId = rsid,
                StopId = sid,
                StopOrden = stopOrden,
                CarrierUserId = uid,
                StoreServiceId = svcTrim,
                TransportServiceLabel = label,
                Status = "pending",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListPublishedForThreadAsync(
        string viewerUserId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var uid = (viewerUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null)
            return null;

        var narrowToCarrierOnly = false;
        if (!ChatService.UserCanSeeThread(uid, thread))
        {
            var canSeeAsCarrier = await db.RouteTramoSubscriptions.AsNoTracking()
                .AnyAsync(x => x.ThreadId == tid && x.CarrierUserId == uid, cancellationToken);
            if (!canSeeAsCarrier)
                return null;
            narrowToCarrierOnly = true;
        }

        var publishedSheets = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null && x.PublishedToPlatform)
            .ToListAsync(cancellationToken);
        if (publishedSheets.Count == 0)
            return [];

        var publishedIds = publishedSheets.Select(x => x.RouteSheetId).ToHashSet(StringComparer.Ordinal);
        var payloads = publishedSheets.ToDictionary(x => x.RouteSheetId, x => x.Payload, StringComparer.Ordinal);

        var rowsQuery = db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && publishedIds.Contains(x.RouteSheetId));
        if (narrowToCarrierOnly)
            rowsQuery = rowsQuery.Where(x => x.CarrierUserId == uid);

        var rows = await rowsQuery
            .OrderBy(x => x.RouteSheetId)
            .ThenBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        return await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
    }

    public async Task<IReadOnlyList<RouteTramoSubscriptionItemDto>?> ListForCarrierByEmergentPublicationAsync(
        string carrierUserId,
        string emergentOfferId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var eid = (emergentOfferId ?? "").Trim();
        if (uid.Length < 2 || eid.Length < 4 || !RecommendationBatchOfferLoader.IsEmergentPublicationId(eid))
            return null;

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eid && x.RetractedAtUtc == null, cancellationToken);
        if (em is null)
            return null;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == em.ThreadId
                    && x.RouteSheetId == em.RouteSheetId
                    && x.DeletedAtUtc == null
                    && x.PublishedToPlatform,
                cancellationToken);
        if (sheetRow is null)
            return [];

        var rows = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == em.ThreadId && x.RouteSheetId == em.RouteSheetId && x.CarrierUserId == uid)
            .OrderBy(x => x.StopOrden)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var payloads = new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal)
        {
            [em.RouteSheetId] = sheetRow.Payload,
        };
        return await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
    }

    private async Task<List<RouteTramoSubscriptionItemDto>> ToSubscriptionItemDtosAsync(
        List<RouteTramoSubscriptionRow> rows,
        Dictionary<string, RouteSheetPayload> payloads,
        CancellationToken cancellationToken)
    {
        var carrierIds = rows.Select(x => x.CarrierUserId).Distinct(StringComparer.Ordinal).ToList();
        var accounts = await db.UserAccounts.AsNoTracking()
            .Where(x => carrierIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal, cancellationToken);

        var svcIds = rows
            .Select(x => x.StoreServiceId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var svcStores = svcIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.StoreServices.AsNoTracking()
                .Where(s => svcIds.Contains(s.Id) && s.DeletedAtUtc == null)
                .Select(s => new { s.Id, s.StoreId })
                .ToDictionaryAsync(x => x.Id, x => x.StoreId, StringComparer.Ordinal, cancellationToken);

        static string Digits(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return new string(s.Where(char.IsDigit).ToArray());
        }

        var list = new List<RouteTramoSubscriptionItemDto>(rows.Count);
        foreach (var r in rows)
        {
            payloads.TryGetValue(r.RouteSheetId, out var payload);
            var parada = (payload?.Paradas ?? []).FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), r.StopId, StringComparison.Ordinal));

            var orden = parada?.Orden > 0 ? parada.Orden : r.StopOrden;
            var origen = (parada?.Origen ?? "").Trim();
            var destino = (parada?.Destino ?? "").Trim();
            if (origen.Length == 0 && destino.Length == 0)
            {
                origen = "—";
                destino = "—";
            }

            accounts.TryGetValue(r.CarrierUserId, out var acc);
            var display = string.IsNullOrWhiteSpace(acc?.DisplayName) ? "Transportista" : acc!.DisplayName.Trim();
            var phone = (acc?.PhoneDisplay ?? "").Trim();
            if (phone.Length == 0 && !string.IsNullOrWhiteSpace(acc?.PhoneDigits))
                phone = acc!.PhoneDigits!.Trim();
            var trust = acc?.TrustScore ?? 0;

            var status = (r.Status ?? "pending").Trim().ToLowerInvariant();
            if (status is not "confirmed" and not "rejected")
            {
                var telStop = Digits(parada?.TelefonoTransportista);
                var telCarrier = Digits(acc?.PhoneDigits ?? acc?.PhoneDisplay);
                if (telStop.Length >= 6 && telCarrier.Length >= 6
                    && string.Equals(telStop, telCarrier, StringComparison.Ordinal))
                    status = "confirmed";
            }

            var createdMs = r.CreatedAtUtc.ToUnixTimeMilliseconds();
            string? svcStore = null;
            if (!string.IsNullOrWhiteSpace(r.StoreServiceId)
                && svcStores.TryGetValue(r.StoreServiceId.Trim(), out var st))
                svcStore = st;

            list.Add(new RouteTramoSubscriptionItemDto(
                r.RouteSheetId,
                r.StopId,
                orden,
                r.CarrierUserId,
                display,
                phone,
                trust,
                r.StoreServiceId,
                r.TransportServiceLabel,
                status,
                origen,
                destino,
                createdMs,
                svcStore));
        }

        return list;
    }

    public async Task<int?> AcceptCarrierPendingOnSheetAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        CancellationToken cancellationToken = default)
    {
        var aid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        if (aid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || cid.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;
        if (!string.Equals(thread.BuyerUserId, aid, StringComparison.Ordinal)
            && !string.Equals(thread.SellerUserId, aid, StringComparison.Ordinal))
            return null;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return null;

        var subs = await db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.CarrierUserId == cid)
            .ToListAsync(cancellationToken);

        var toConfirm = subs.Where(r =>
        {
            var st = (r.Status ?? "pending").Trim().ToLowerInvariant();
            return st is not "confirmed" and not "rejected";
        }).ToList();

        if (toConfirm.Count == 0)
            return subs.Count > 0 ? 0 : null;

        var carrierAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cid, cancellationToken);
        var phone = (carrierAcc?.PhoneDisplay ?? "").Trim();
        if (phone.Length == 0 && !string.IsNullOrWhiteSpace(carrierAcc?.PhoneDigits))
            phone = carrierAcc!.PhoneDigits!.Trim();

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toConfirm)
        {
            sub.Status = "confirmed";
            sub.UpdatedAtUtc = now;
            var parada = payload.Paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sub.StopId, StringComparison.Ordinal));
            if (parada is not null && phone.Length > 0)
                parada.TelefonoTransportista = phone;
        }

        sheetRow.Payload = payload;
        sheetRow.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == aid, cancellationToken);
        var deciderLabel = string.IsNullOrWhiteSpace(actorAcc?.DisplayName)
            ? "Participante"
            : actorAcc!.DisplayName.Trim();
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview =
            $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        await chat.NotifyRouteTramoSubscriptionAcceptedAsync(
            cid,
            tid,
            preview,
            deciderLabel,
            deciderTrust,
            aid,
            cancellationToken);

        return toConfirm.Count;
    }
}
