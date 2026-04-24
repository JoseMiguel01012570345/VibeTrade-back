using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Features.Recommendations;

namespace VibeTrade.Backend.Features.Chat;

public sealed class RouteTramoSubscriptionService(AppDbContext db, IChatService chat) : IRouteTramoSubscriptionService
{
    /// <summary>Alineado con <c>CARRIER_ROUTE_EXIT_TRUST_PENALTY</c> en el cliente (abandono con tramos confirmados y ruta no entregada).</summary>
    private const int CarrierRouteExitTrustPenalty = 3;
    public async Task RecordSubscriptionRequestAsync(
        string threadId,
        string routeSheetId,
        string stopId,
        int stopOrden,
        string carrierUserId,
        string? storeServiceId,
        string transportServiceLabel,
        string? carrierContactPhone = null,
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

        var snap = (carrierContactPhone ?? "").Trim();
        if (snap.Length > 40)
            snap = snap[..40];
        if (snap.Length == 0)
            snap = null;

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
            if (snap is not null)
                existing.CarrierPhoneSnapshot = snap;
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
                CarrierPhoneSnapshot = snap,
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

        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken))
            return null;
        var narrowToCarrierOnly = !ChatService.UserCanSeeThread(uid, thread);

        var publishedSheets = await db.ChatRouteSheets.AsNoTracking()
            .Where(x => x.ThreadId == tid && x.DeletedAtUtc == null && x.PublishedToPlatform)
            .ToListAsync(cancellationToken);
        if (publishedSheets.Count == 0)
            return [];

        var publishedIds = publishedSheets.Select(x => x.RouteSheetId).ToHashSet(StringComparer.Ordinal);
        var payloads = publishedSheets.ToDictionary(x => x.RouteSheetId, x => x.Payload, StringComparer.Ordinal);

        var rowsQuery = db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x => x.ThreadId == tid && publishedIds.Contains(x.RouteSheetId));

        var rows = await rowsQuery
            .OrderBy(x => x.RouteSheetId)
            .ThenBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var list = await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
        if (narrowToCarrierOnly)
            list = NarrowSubscriptionDtosForCarrierViewer(uid, list);
        return list;
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
            .Where(x => x.ThreadId == em.ThreadId && x.RouteSheetId == em.RouteSheetId)
            .OrderBy(x => x.StopOrden)
            .ThenBy(x => x.CarrierUserId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var payloads = new Dictionary<string, RouteSheetPayload>(StringComparer.Ordinal)
        {
            [em.RouteSheetId] = sheetRow.Payload,
        };
        var list = await ToSubscriptionItemDtosAsync(rows, payloads, cancellationToken);
        return NarrowSubscriptionDtosForCarrierViewer(uid, list);
    }

    /// <summary>
    /// Visibilidad transportista: confirmados de todos + filas propias (pendientes / rechazadas / retiradas).
    /// </summary>
    private static List<RouteTramoSubscriptionItemDto> NarrowSubscriptionDtosForCarrierViewer(
        string viewerUserId,
        List<RouteTramoSubscriptionItemDto> dtos)
    {
        var v = (viewerUserId ?? "").Trim();
        if (v.Length < 2)
            return [];
        return dtos
            .Where(dto =>
                string.Equals((dto.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase)
                || ChatService.UserIdsMatchLoose(v, dto.CarrierUserId))
            .ToList();
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
            if (phone.Length == 0)
                phone = (r.CarrierPhoneSnapshot ?? "").Trim();
            if (phone.Length == 0 && parada is not null)
                phone = (parada.TelefonoTransportista ?? "").Trim();
            var trust = acc?.TrustScore ?? 0;

            var status = (r.Status ?? "pending").Trim().ToLowerInvariant();

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
        string? stopId = null,
        CancellationToken cancellationToken = default)
    {
        var aid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        var stopRestrict = (stopId ?? "").Trim();
        if (aid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || cid.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;
        if (!string.Equals(thread.SellerUserId, aid, StringComparison.Ordinal))
            return null;

        var sheetRow = await db.ChatRouteSheets
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return null;

        var subsQuery = db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.CarrierUserId == cid);
        if (stopRestrict.Length > 0)
            subsQuery = subsQuery.Where(x => x.StopId == stopRestrict);
        var subs = await subsQuery.ToListAsync(cancellationToken);
        if (stopRestrict.Length > 0 && subs.Count == 0)
            return null;

        var toConfirm = subs.Where(r =>
        {
            var st = (r.Status ?? "pending").Trim().ToLowerInvariant();
            return st is not "confirmed" and not "rejected" and not "withdrawn";
        }).ToList();

        if (toConfirm.Count == 0)
            return subs.Count > 0 ? 0 : null;

        // `Status` se persiste en minúsculas; evitar `string.Equals(..., StringComparison)` en IQueryable (EF no lo traduce a SQL).
        var stopsTakenByOthers = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.CarrierUserId != cid
                && x.Status == "confirmed")
            .Select(x => x.StopId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var taken = stopsTakenByOthers.ToHashSet(StringComparer.Ordinal);

        var toConfirmFiltered = toConfirm.Where(s => !taken.Contains(s.StopId)).ToList();
        if (toConfirmFiltered.Count == 0)
            throw new TramoSubscriptionAcceptConflictException(
                "Los tramos pendientes de este transportista ya tienen otro transportista confirmado.");

        toConfirm = toConfirmFiltered;

        var carrierAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cid, cancellationToken);
        var accountPhone = (carrierAcc?.PhoneDisplay ?? "").Trim();
        if (accountPhone.Length == 0 && !string.IsNullOrWhiteSpace(carrierAcc?.PhoneDigits))
            accountPhone = carrierAcc!.PhoneDigits!.Trim();

        var payload = sheetRow.Payload;
        payload.Paradas ??= new List<RouteStopPayload>();
        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toConfirm)
        {
            sub.Status = "confirmed";
            sub.UpdatedAtUtc = now;
            var parada = payload.Paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sub.StopId, StringComparison.Ordinal));
            var tel = accountPhone;
            if (tel.Length == 0)
                tel = (sub.CarrierPhoneSnapshot ?? "").Trim();
            if (tel.Length == 0 && parada is not null)
                tel = (parada.TelefonoTransportista ?? "").Trim();
            if (parada is not null && tel.Length > 0)
                parada.TelefonoTransportista = tel;
        }

        // EF no detecta cambios en objetos anidados bajo Payload con HasConversion JSON;
        // reasignar el mismo reference no persiste. Clonar vía JSON alinea con RouteSheetJson.Options.
        sheetRow.Payload = JsonSerializer.Deserialize<RouteSheetPayload>(
                JsonSerializer.Serialize(payload, RouteSheetJson.Options),
                RouteSheetJson.Options)
            ?? payload;
        sheetRow.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        var emRow = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.RetractedAtUtc == null,
                cancellationToken);
        var emergentPubId = string.IsNullOrWhiteSpace(emRow?.Id) ? null : emRow!.Id.Trim();

        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == aid, cancellationToken);
        var deciderLabel = string.IsNullOrWhiteSpace(actorAcc?.DisplayName)
            ? "Participante"
            : actorAcc!.DisplayName.Trim();
        var deciderTrust = actorAcc?.TrustScore ?? 0;
        var preview =
            $"{deciderLabel} confirmó tu servicio de transporte en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        var carrierLabel = string.IsNullOrWhiteSpace(carrierAcc?.DisplayName)
            ? "El transportista"
            : carrierAcc!.DisplayName.Trim();
        var sellerInboxPreview =
            $"Confirmaste el servicio de transporte de {carrierLabel} en esta operación. Abrí el chat para coordinar la hoja de ruta.";

        await chat.NotifyRouteTramoSubscriptionAcceptedAsync(
            cid,
            tid,
            preview,
            deciderLabel,
            deciderTrust,
            aid,
            sellerInboxUserId: aid,
            sellerInboxPreview: sellerInboxPreview,
            sellerInboxSubjectLabel: carrierLabel,
            sellerInboxSubjectTrust: carrierAcc?.TrustScore ?? 0,
            cancellationToken: cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            tid,
            rsid,
            "accept",
            aid,
            emergentPubId,
            cancellationToken);

        return toConfirm.Count;
    }

    public async Task<int?> RejectCarrierPendingOnSheetAsync(
        string actorUserId,
        string threadId,
        string routeSheetId,
        string carrierUserId,
        string? stopId = null,
        CancellationToken cancellationToken = default)
    {
        var aid = (actorUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var cid = (carrierUserId ?? "").Trim();
        var stopRestrict = (stopId ?? "").Trim();
        if (aid.Length < 2 || tid.Length < 4 || rsid.Length < 1 || cid.Length < 2)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;
        if (!string.Equals(thread.SellerUserId, aid, StringComparison.Ordinal))
            return null;

        var sheetRow = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken);
        if (sheetRow is null || !sheetRow.PublishedToPlatform)
            return null;

        var subsQuery = db.RouteTramoSubscriptions
            .Where(x => x.ThreadId == tid && x.RouteSheetId == rsid && x.CarrierUserId == cid);
        if (stopRestrict.Length > 0)
            subsQuery = subsQuery.Where(x => x.StopId == stopRestrict);
        var subs = await subsQuery.ToListAsync(cancellationToken);
        if (stopRestrict.Length > 0 && subs.Count == 0)
            return null;

        var toReject = subs.Where(r =>
        {
            var st = (r.Status ?? "pending").Trim().ToLowerInvariant();
            return st is not "confirmed" and not "rejected" and not "withdrawn";
        }).ToList();

        if (toReject.Count == 0)
            return subs.Count > 0 ? 0 : null;

        var now = DateTimeOffset.UtcNow;
        foreach (var sub in toReject)
        {
            sub.Status = "rejected";
            sub.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var em = await db.EmergentOffers.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.RetractedAtUtc == null,
                cancellationToken);
        var routeOfferId = string.IsNullOrWhiteSpace(em?.Id) ? null : em!.Id.Trim();

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.StoreId, cancellationToken);
        var actorAcc = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == aid, cancellationToken);
        var sellerLabel = !string.IsNullOrWhiteSpace(store?.Name) ? store!.Name.Trim()
            : string.IsNullOrWhiteSpace(actorAcc?.DisplayName) ? "Vendedor"
            : actorAcc!.DisplayName.Trim();
        var sellerTrust = store?.TrustScore ?? actorAcc?.TrustScore ?? 0;
        var preview =
            $"{sellerLabel} rechazó tu solicitud de transporte en un tramo de la hoja de ruta publicada. Podés revisar la oferta y los tramos disponibles.";

        await chat.NotifyRouteTramoSubscriptionRejectedAsync(
            cid,
            tid,
            preview,
            sellerLabel,
            sellerTrust,
            aid,
            routeOfferId,
            cancellationToken);

        await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
            tid,
            rsid,
            "reject",
            aid,
            routeOfferId,
            cancellationToken);

        return toReject.Count;
    }

    public async Task<CarrierWithdrawFromThreadResult?> WithdrawCarrierFromThreadAsync(
        string carrierUserId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var uid = (carrierUserId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4)
            return null;

        var thread = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken);
        if (thread is null || thread.DeletedAtUtc is not null)
            return null;
        if (!await chat.UserCanAccessThreadRowAsync(uid, thread, cancellationToken))
            return null;
        if (ChatService.UserCanSeeThread(uid, thread))
            return null;

        var subs = await db.RouteTramoSubscriptions
            .Where(x =>
                x.ThreadId == tid
                && x.CarrierUserId == uid
                && x.Status != "withdrawn")
            .ToListAsync(cancellationToken);
        if (subs.Count == 0)
            return null;

        var hadConfirmed = subs.Exists(x =>
            string.Equals((x.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));

        var distinctSheetIds = subs.Select(x => x.RouteSheetId).Distinct().ToList();
        var anyRouteIncomplete = false;
        foreach (var rsid in distinctSheetIds)
        {
            var sh = await db.ChatRouteSheets.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                    cancellationToken);
            if (sh is null)
                continue;
            var est = (sh.Payload?.Estado ?? "").Trim().ToLowerInvariant();
            if (est != "entregada")
                anyRouteIncomplete = true;
        }

        var applyTrustPenalty = hadConfirmed && anyRouteIncomplete;
        var now = DateTimeOffset.UtcNow;

        foreach (var grp in subs.GroupBy(x => x.RouteSheetId))
        {
            var rsid = grp.Key;
            var sheetRow = await db.ChatRouteSheets
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                    cancellationToken);
            if (sheetRow is null)
                continue;
            var payload = sheetRow.Payload;
            payload.Paradas ??= new List<RouteStopPayload>();
            // Una fila de suscripción → como mucho un tramo: evita borrar teléfonos de otros transportistas
            // si varias paradas comparten Id vacío/erróneo o hay duplicados en JSON.
            foreach (var sub in grp)
            {
                var sid = (sub.StopId ?? "").Trim();
                RouteStopPayload? parada = null;
                if (sid.Length > 0)
                {
                    parada = payload.Paradas.FirstOrDefault(p =>
                        string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
                }
                if (parada is null && sub.StopOrden > 0)
                {
                    parada = payload.Paradas.FirstOrDefault(p => p.Orden == sub.StopOrden);
                }
                if (parada is not null)
                    parada.TelefonoTransportista = null;
            }

            sheetRow.Payload = JsonSerializer.Deserialize<RouteSheetPayload>(
                    JsonSerializer.Serialize(payload, RouteSheetJson.Options),
                    RouteSheetJson.Options)
                ?? payload;
            sheetRow.UpdatedAtUtc = now;
        }

        foreach (var s in subs)
        {
            s.Status = "withdrawn";
            s.UpdatedAtUtc = now;
        }

        int? trustScoreAfterPenalty = null;
        if (applyTrustPenalty)
        {
            var acc = await db.UserAccounts
                .FirstOrDefaultAsync(x => x.Id == uid, cancellationToken);
            if (acc is not null)
            {
                acc.TrustScore = Math.Max(-10_000, acc.TrustScore - CarrierRouteExitTrustPenalty);
                trustScoreAfterPenalty = acc.TrustScore;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var rsid in distinctSheetIds)
        {
            var emRow = await db.EmergentOffers.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ThreadId == tid && x.RouteSheetId == rsid && x.RetractedAtUtc == null,
                    cancellationToken);
            var emergentPubId = string.IsNullOrWhiteSpace(emRow?.Id) ? null : emRow!.Id.Trim();
            await chat.BroadcastRouteTramoSubscriptionsChangedAsync(
                tid,
                rsid,
                "withdraw",
                uid,
                emergentPubId,
                cancellationToken);
        }

        return new CarrierWithdrawFromThreadResult(subs.Count, applyTrustPenalty, trustScoreAfterPenalty);
    }
}
