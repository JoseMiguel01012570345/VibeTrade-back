using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Titularidad operativa del tramo: solo un eslabón activo en cadena (pago → tramo 1; luego promoción / ceder).
/// </summary>
public static class RouteLegOwnershipChain
{
    private static int IndexOfStop(IReadOnlyList<string> ordered, string stopId)
    {
        var sid = (stopId ?? "").Trim();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals((ordered[i] ?? "").Trim(), sid, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    public static List<string> OrderedStopIds(RouteSheetPayload? payload)
    {
        if (payload?.Paradas is not { Count: > 0 } list)
            return [];
        return list
            .OrderBy(p => p.Orden)
            .Select(p => (p.Id ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    /// <summary>Primer tramo en orden de hoja que está en el conjunto pagado.</summary>
    public static string? FirstPaidStopId(IReadOnlyList<string> ordered, HashSet<string> paidStopIds)
    {
        foreach (var id in ordered)
        {
            if (paidStopIds.Contains(id))
                return id;
        }

        return null;
    }

    public static bool IsPaidLikeState(string state)
    {
        var s = (state ?? "").Trim();
        return s is RouteStopDeliveryStates.Paid
            or RouteStopDeliveryStates.InTransit
            or RouteStopDeliveryStates.DeliveredPendingEvidence
            or RouteStopDeliveryStates.EvidenceSubmitted
            or RouteStopDeliveryStates.EvidenceAccepted
            or RouteStopDeliveryStates.EvidenceRejected;
    }

    public static bool PreviousLegEvidenceAccepted(
        IReadOnlyList<string> ordered,
        IReadOnlyDictionary<string, RouteStopDeliveryRow> byStop,
        string stopId)
    {
        var sid = (stopId ?? "").Trim();
        var idx = IndexOfStop(ordered, sid);
        if (idx <= 0)
            return true;
        var prevId = ordered[idx - 1];
        return byStop.TryGetValue(prevId.Trim(), out var prev)
               && string.Equals(prev.State, RouteStopDeliveryStates.EvidenceAccepted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tras pago o alta de carrier: solo se asigna titular si es el primer tramo o el anterior ya cerró evidencia.
    /// </summary>
    public static bool MayAssignOperationalOwner(
        IReadOnlyList<string> ordered,
        IReadOnlyDictionary<string, RouteStopDeliveryRow> byStop,
        string stopId)
    {
        var sid = (stopId ?? "").Trim();
        if (ordered.Count == 0 || string.IsNullOrWhiteSpace(sid))
            return false;
        var idx = IndexOfStop(ordered, sid);
        if (idx < 0)
            return false;
        if (idx == 0)
            return true;
        return PreviousLegEvidenceAccepted(ordered, byStop, sid);
    }

    /// <summary>
    /// Tras aceptar evidencia del tramo <paramref name="acceptedStopId"/>, otorga titular al transportista confirmado del siguiente tramo pagado.
    /// </summary>
    public static async Task GrantNextLegOwnerAfterEvidenceAcceptedAsync(
        AppDbContext db,
        string threadId,
        string agreementId,
        string routeSheetId,
        string acceptedStopId,
        RouteSheetPayload payload,
        CancellationToken cancellationToken)
    {
        var tid = (threadId ?? "").Trim();
        var aid = (agreementId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var acc = (acceptedStopId ?? "").Trim();
        if (tid.Length < 4 || aid.Length < 8 || rsid.Length < 1 || acc.Length < 1)
            return;

        var ordered = OrderedStopIds(payload);
        var idx = IndexOfStop(ordered, acc);
        if (idx < 0 || idx >= ordered.Count - 1)
            return;

        var nextId = ordered[idx + 1].Trim();
        if (nextId.Length == 0)
            return;

        var carrier = await db.RouteTramoSubscriptions.AsNoTracking()
            .Where(x =>
                x.ThreadId == tid
                && x.RouteSheetId == rsid
                && x.StopId == nextId
                && x.Status == "confirmed")
            .Select(x => x.CarrierUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var carrierUid = (carrier ?? "").Trim();
        if (carrierUid.Length < 2)
            return;

        var nextRow = await db.RouteStopDeliveries.FirstOrDefaultAsync(
                x =>
                    x.ThreadId == tid
                    && x.TradeAgreementId == aid
                    && x.RouteSheetId == rsid
                    && x.RouteStopId == nextId,
                cancellationToken)
            .ConfigureAwait(false);
        if (nextRow is null || !IsPaidLikeState(nextRow.State))
            return;
        if (!string.IsNullOrWhiteSpace(nextRow.CurrentOwnerUserId))
            return;

        var now = DateTimeOffset.UtcNow;
        nextRow.CurrentOwnerUserId = carrierUid;
        nextRow.OwnershipGrantedAtUtc = now;
        nextRow.UpdatedAtUtc = now;

        db.CarrierOwnershipEvents.Add(new CarrierOwnershipEventRow
        {
            Id = "coe_" + Guid.NewGuid().ToString("N"),
            ThreadId = tid,
            RouteSheetId = rsid,
            RouteStopId = nextId,
            CarrierUserId = carrierUid,
            Action = CarrierOwnershipActions.Granted,
            AtUtc = now,
            Reason = "prior_leg_evidence_accepted",
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Índice del tramo en la hoja o -1.</summary>
    public static int StopIndex(IReadOnlyList<string> ordered, string stopId) =>
        IndexOfStop(ordered, stopId);

    /// <summary>Siguiente tramo en orden de ruta; null si no hay.</summary>
    public static string? NextStopId(IReadOnlyList<string> ordered, string stopId)
    {
        var idx = StopIndex(ordered, stopId);
        if (idx < 0 || idx >= ordered.Count - 1)
            return null;
        var n = ordered[idx + 1].Trim();
        return n.Length > 0 ? n : null;
    }
}
