using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Dtos;
using VibeTrade.Backend.Features.Chat.Interfaces;

namespace VibeTrade.Backend.Features.Logistics;

public sealed class CarrierDeliveryEvidenceService(IChatService chat, AppDbContext db) : ICarrierDeliveryEvidenceService
{
    public async Task<(int StatusCode, string? Error, CarrierDeliveryEvidenceDto? Data)> UpsertAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        UpsertCarrierDeliveryEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var uid = (userId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1)
            return (StatusCodes.Status404NotFound, null, null);

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null, null);

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
            return (StatusCodes.Status404NotFound, null, null);

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
            return (StatusCodes.Status404NotFound, null, null);

        var isCurrentOperationalOwner = string.Equals(delivery.CurrentOwnerUserId, uid, StringComparison.Ordinal);
        if (!isCurrentOperationalOwner)
        {
            // Titular actual: puede enviar evidencia del tramo en curso. Si ya no es titular, solo si cedió
            // explícitamente la titularidad en este tramo (handoff a otro transportista).
            var cededOwnershipOnThisLeg = await db.CarrierOwnershipEvents.AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ThreadId == tid
                        && x.RouteSheetId == rsid
                        && x.RouteStopId == sid
                        && x.CarrierUserId == uid
                        && x.Action == CarrierOwnershipActions.Released
                        && x.Reason == "carrier_cede",
                    cancellationToken)
                .ConfigureAwait(false);
            if (!cededOwnershipOnThisLeg)
                return (StatusCodes.Status403Forbidden,
                    "Solo el titular del tramo o quien ya cedió la titularidad aquí puede enviar evidencia.",
                    null);
        }

        var payloadUpsert = await LoadPayloadAsync(tid, rsid, cancellationToken).ConfigureAwait(false);
        var orderedUpsert = RouteLegOwnershipChain.OrderedStopIds(payloadUpsert);
        if (orderedUpsert.Count > 0)
        {
            var siblingDeliveries = await db.RouteStopDeliveries.AsNoTracking()
                .Where(x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var byStopUpsert = siblingDeliveries.ToDictionary(x => x.RouteStopId.Trim(), StringComparer.Ordinal);
            if (!RouteLegOwnershipChain.PreviousLegEvidenceAccepted(orderedUpsert, byStopUpsert, sid))
                return (StatusCodes.Status400BadRequest,
                    "Este tramo no está habilitado todavía: el anterior debe tener evidencia aceptada y la titularidad correspondiente.",
                    null);
        }

        if (delivery.State is RouteStopDeliveryStates.RefundedExpired
            or RouteStopDeliveryStates.RefundedCarrierExit
            or RouteStopDeliveryStates.Unpaid)
            return (StatusCodes.Status400BadRequest, "Este tramo no admite evidencias en su estado actual.", null);

        var now = DateTimeOffset.UtcNow;
        var ev = await db.CarrierDeliveryEvidences
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.TradeAgreementId == aid && x.RouteSheetId == rsid && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);

        if (ev is not null &&
            string.Equals(ev.Status, ServiceEvidenceStatuses.Accepted, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "Evidencia ya aceptada: no se puede editar.", null);

        var nextStatus = body.Submit ? ServiceEvidenceStatuses.Submitted : ServiceEvidenceStatuses.Draft;
        var norm = NormalizeEvidence(body.Text, body.Attachments);

        if (ev is null)
        {
            ev = new CarrierDeliveryEvidenceRow
            {
                Id = "cde_" + Guid.NewGuid().ToString("N"),
                ThreadId = tid,
                TradeAgreementId = aid,
                RouteSheetId = rsid,
                RouteStopId = sid,
                CarrierUserId = uid,
                Text = norm.Text,
                Attachments = norm.Atts,
                LastSubmittedText = "",
                LastSubmittedAttachments = [],
                Status = nextStatus,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                DeadlineAtUtc = delivery.EvidenceDeadlineAtUtc,
            };
            if (body.Submit)
            {
                ev.LastSubmittedText = norm.Text;
                ev.LastSubmittedAttachments = norm.Atts.ToList();
                ev.LastSubmittedAtUtc = now;
            }

            db.CarrierDeliveryEvidences.Add(ev);
        }
        else
        {
            if (body.Submit)
            {
                var lastNorm = NormalizeEvidence(ev.LastSubmittedText, ev.LastSubmittedAttachments);
                if (EvidenceEquals(lastNorm, norm))
                    return (StatusCodes.Status400BadRequest, "No hay cambios desde la última evidencia enviada.", null);
                ev.LastSubmittedText = norm.Text;
                ev.LastSubmittedAttachments = norm.Atts.ToList();
                ev.LastSubmittedAtUtc = now;
            }

            ev.Text = norm.Text;
            ev.Attachments = norm.Atts;
            ev.Status = nextStatus;
            ev.UpdatedAtUtc = now;
        }

        if (body.Submit)
        {
            delivery.State = RouteStopDeliveryStates.EvidenceSubmitted;
            delivery.UpdatedAtUtc = now;
        }
        else if (delivery.State == RouteStopDeliveryStates.InTransit || delivery.State == RouteStopDeliveryStates.Paid)
        {
            delivery.State = RouteStopDeliveryStates.DeliveredPendingEvidence;
            delivery.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var notice = body.Submit
            ? "Evidencia de entrega enviada por el transportista para un tramo de la hoja de ruta."
            : "Evidencia de entrega guardada (borrador) por el transportista.";
        await chat.PostAutomatedSystemThreadNoticeAsync(tid, notice, cancellationToken).ConfigureAwait(false);

        return (StatusCodes.Status200OK, null, Map(ev));
    }

    public async Task<(int StatusCode, string? Error)> DecideAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        DecideCarrierDeliveryEvidenceRequest body,
        CancellationToken cancellationToken)
    {
        var uid = (userId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1)
            return (StatusCodes.Status404NotFound, null);

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null);

        var isSeller = string.Equals(t.SellerUserId, uid, StringComparison.Ordinal);
        if (!isSeller)
            return (StatusCodes.Status403Forbidden, "Solo la tienda puede aceptar o rechazar esta evidencia.");

        var ev = await db.CarrierDeliveryEvidences.FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.TradeAgreementId == aid && x.RouteSheetId == rsid && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (ev is null) return (StatusCodes.Status400BadRequest, "No hay evidencia para decidir.");
        if (!string.Equals(ev.Status, ServiceEvidenceStatuses.Submitted, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ev.Status, ServiceEvidenceStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status400BadRequest, "La evidencia no está en estado decidible.");

        var delivery = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null) return (StatusCodes.Status404NotFound, null);

        var d = (body.Decision ?? "").Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        if (d is "accept" or "accepted")
        {
            ev.Status = ServiceEvidenceStatuses.Accepted;
            ev.DecidedAtUtc = now;
            ev.DecidedByUserId = uid;
            ev.UpdatedAtUtc = now;

            delivery.State = RouteStopDeliveryStates.EvidenceAccepted;
            delivery.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var payloadDecide = await LoadPayloadAsync(tid, rsid, cancellationToken).ConfigureAwait(false);
            await RouteLegOwnershipChain
                .GrantNextLegOwnerAfterEvidenceAcceptedAsync(
                    db,
                    tid,
                    aid,
                    rsid,
                    sid,
                    payloadDecide,
                    cancellationToken)
                .ConfigureAwait(false);

            var orderedForNotify = RouteLegOwnershipChain.OrderedStopIds(payloadDecide);
            var notifyPaidStops = new HashSet<string>(StringComparer.Ordinal) { sid.Trim() };
            var nextAfterAccept = RouteLegOwnershipChain.NextStopId(orderedForNotify, sid);
            var nextTrim = (nextAfterAccept ?? "").Trim();
            if (nextTrim.Length > 0)
            {
                var nextPaid = await db.RouteStopDeliveries.AsNoTracking().AnyAsync(
                        x =>
                            x.ThreadId == tid
                            && x.TradeAgreementId == aid
                            && x.RouteSheetId == rsid
                            && x.RouteStopId == nextTrim
                            && (x.State == RouteStopDeliveryStates.Paid
                                || x.State == RouteStopDeliveryStates.InTransit
                                || x.State == RouteStopDeliveryStates.DeliveredPendingEvidence
                                || x.State == RouteStopDeliveryStates.EvidenceSubmitted
                                || x.State == RouteStopDeliveryStates.EvidenceAccepted
                                || x.State == RouteStopDeliveryStates.EvidenceRejected),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (nextPaid)
                    notifyPaidStops.Add(nextTrim);
            }

            await RouteLegHandoffNotifications.NotifyPaidStopsAsync(
                    db,
                    chat,
                    tid,
                    aid,
                    rsid,
                    payloadDecide,
                    notifyPaidStops,
                    cancellationToken)
                .ConfigureAwait(false);

            await chat.PostAutomatedSystemThreadNoticeAsync(
                    tid,
                    "Evidencia de entrega aceptada para un tramo de la hoja de ruta.",
                    cancellationToken)
                .ConfigureAwait(false);
            return (StatusCodes.Status200OK, null);
        }

        if (d is "reject" or "rejected")
        {
            ev.Status = ServiceEvidenceStatuses.Rejected;
            ev.DecidedAtUtc = now;
            ev.DecidedByUserId = uid;
            ev.UpdatedAtUtc = now;

            delivery.State = RouteStopDeliveryStates.EvidenceRejected;
            delivery.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await chat.PostAutomatedSystemThreadNoticeAsync(
                    tid,
                    "Evidencia de entrega rechazada: el transportista puede reenviar adjuntos.",
                    cancellationToken)
                .ConfigureAwait(false);
            return (StatusCodes.Status200OK, null);
        }

        return (StatusCodes.Status400BadRequest, "Decisión inválida.");
    }

    public async Task<(int StatusCode, string? Error, CarrierDeliveryEvidenceDto? Data)> GetAsync(
        string userId,
        string threadId,
        string agreementId,
        string routeSheetId,
        string routeStopId,
        CancellationToken cancellationToken)
    {
        var uid = (userId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        if (uid.Length < 2 || tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || sid.Length < 1)
            return (StatusCodes.Status404NotFound, null, null);

        var t = await db.ChatThreads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
            .ConfigureAwait(false);
        if (t is null) return (StatusCodes.Status404NotFound, null, null);
        if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
            return (StatusCodes.Status404NotFound, null, null);

        var ev = await db.CarrierDeliveryEvidences.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.TradeAgreementId == aid && x.RouteSheetId == rsid && x.RouteStopId == sid,
                cancellationToken)
            .ConfigureAwait(false);
        if (ev is null)
            return (StatusCodes.Status404NotFound, null, null);

        var isCarrierAuthor = string.Equals(ev.CarrierUserId, uid, StringComparison.Ordinal);
        var draft = string.Equals(ev.Status, ServiceEvidenceStatuses.Draft, StringComparison.OrdinalIgnoreCase);
        if (draft && !isCarrierAuthor)
            return (StatusCodes.Status404NotFound, null, null);

        return (StatusCodes.Status200OK, null, Map(ev));
    }

    private async Task<Data.RouteSheets.RouteSheetPayload> LoadPayloadAsync(
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken)
    {
        var row = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == threadId && x.RouteSheetId == routeSheetId && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        return row?.Payload ?? new Data.RouteSheets.RouteSheetPayload();
    }

    private static CarrierDeliveryEvidenceDto Map(CarrierDeliveryEvidenceRow ev) =>
        new(
            ev.Id,
            ev.CarrierUserId,
            ev.Text,
            ev.Attachments,
            ev.LastSubmittedText,
            ev.LastSubmittedAttachments,
            ev.LastSubmittedAtUtc,
            ev.Status,
            ev.CreatedAtUtc,
            ev.UpdatedAtUtc,
            ev.DecidedAtUtc,
            ev.DecidedByUserId,
            ev.DeadlineAtUtc);

    private sealed record NormalizedEvidence(string Text, List<ServiceEvidenceAttachmentBody> Atts);

    private static NormalizedEvidence NormalizeEvidence(string? text, List<ServiceEvidenceAttachmentBody>? attachments)
    {
        var t = (text ?? "").Trim();
        var a = (attachments ?? [])
            .Select(x => new ServiceEvidenceAttachmentBody(
                (x.Id ?? "").Trim(),
                (x.Url ?? "").Trim(),
                (x.FileName ?? "").Trim(),
                (x.Kind ?? "").Trim()))
            .Where(x => x.Url.Length > 0)
            .OrderBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new NormalizedEvidence(t, a);
    }

    private static bool EvidenceEquals(NormalizedEvidence a, NormalizedEvidence b)
    {
        if (!string.Equals(a.Text, b.Text, StringComparison.Ordinal)) return false;
        if (a.Atts.Count != b.Atts.Count) return false;
        for (var i = 0; i < a.Atts.Count; i++)
        {
            var x = a.Atts[i];
            var y = b.Atts[i];
            if (!string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
