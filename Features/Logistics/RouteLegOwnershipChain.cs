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

    /// <summary>Índice del tramo en la hoja o -1.</summary>
    public static int StopIndex(IReadOnlyList<string> ordered, string stopId) =>
        IndexOfStop(ordered, stopId);

}
