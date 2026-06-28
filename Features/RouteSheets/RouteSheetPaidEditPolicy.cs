using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteSheets;

/// <summary>
/// Excepciones al bloqueo por cobros registrados: reasignar contacto en tramos sin transportista confirmado.
/// </summary>
public static class RouteSheetPaidEditPolicy
{
    public static bool IsCarrierContactOnlyUpdate(
        RouteSheetPayload oldSheet,
        RouteSheetPayload newSheet,
        IReadOnlySet<string> confirmedStopIds)
    {
        if (!SheetHeaderUnchanged(oldSheet, newSheet))
            return false;

        var oldStops = oldSheet.Paradas ?? [];
        var newStops = newSheet.Paradas ?? [];
        if (oldStops.Count != newStops.Count)
            return false;

        var oldById = oldStops
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToDictionary(p => p.Id.Trim(), StringComparer.Ordinal);
        var newById = newStops
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToDictionary(p => p.Id.Trim(), StringComparer.Ordinal);
        if (oldById.Count != newById.Count || !oldById.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(newById.Keys))
            return false;

        var hasVacantContactChange = false;
        foreach (var sid in oldById.Keys)
        {
            var oldP = oldById[sid];
            var newP = newById[sid];
            if (confirmedStopIds.Contains(sid))
            {
                if (!string.Equals(
                        RouteSheetEditAckComputation.RouteStopFingerprint(oldP),
                        RouteSheetEditAckComputation.RouteStopFingerprint(newP),
                        StringComparison.Ordinal))
                    return false;
                continue;
            }

            if (!string.Equals(
                    RouteSheetEditAckComputation.RouteStopFingerprintExcludingPhone(oldP),
                    RouteSheetEditAckComputation.RouteStopFingerprintExcludingPhone(newP),
                    StringComparison.Ordinal))
                return false;

            if (!CarrierContactFieldsEqual(oldP, newP))
                hasVacantContactChange = true;
        }

        return hasVacantContactChange;
    }

    /// <summary>Solo cambia el flag de publicación en plataforma; el resto de la hoja queda igual.</summary>
    public static bool IsPublishToggleOnlyUpdate(
        RouteSheetPayload oldSheet,
        RouteSheetPayload newSheet)
    {
        if (oldSheet.PublicadaPlataforma == newSheet.PublicadaPlataforma)
            return false;
        if (!SheetHeaderUnchangedExcludingPublish(oldSheet, newSheet))
            return false;

        var oldStops = oldSheet.Paradas ?? [];
        var newStops = newSheet.Paradas ?? [];
        if (oldStops.Count != newStops.Count)
            return false;

        var oldById = oldStops
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToDictionary(p => p.Id.Trim(), StringComparer.Ordinal);
        var newById = newStops
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToDictionary(p => p.Id.Trim(), StringComparer.Ordinal);
        if (oldById.Count != newById.Count || !oldById.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(newById.Keys))
            return false;

        foreach (var sid in oldById.Keys)
        {
            if (!string.Equals(
                    RouteSheetEditAckComputation.RouteStopFingerprint(oldById[sid]),
                    RouteSheetEditAckComputation.RouteStopFingerprint(newById[sid]),
                    StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public static bool InvitesTargetOnlyVacantStops(
        IReadOnlyList<RouteSheetPreselectedInvite> invites,
        IReadOnlySet<string> confirmedStopIds)
    {
        if (invites.Count == 0)
            return false;
        foreach (var inv in invites)
        {
            var sid = (inv.StopId ?? "").Trim();
            if (sid.Length == 0 || confirmedStopIds.Contains(sid))
                return false;
        }

        return true;
    }

    private static bool SheetHeaderUnchanged(RouteSheetPayload oldSheet, RouteSheetPayload newSheet)
    {
        if (!SheetHeaderCoreUnchanged(oldSheet, newSheet))
            return false;

        return oldSheet.PublicadaPlataforma == newSheet.PublicadaPlataforma;
    }

    private static bool SheetHeaderUnchangedExcludingPublish(RouteSheetPayload oldSheet, RouteSheetPayload newSheet) =>
        SheetHeaderCoreUnchanged(oldSheet, newSheet);

    private static bool SheetHeaderCoreUnchanged(RouteSheetPayload oldSheet, RouteSheetPayload newSheet)
    {
        if (!TrimmedEqual(oldSheet.Titulo, newSheet.Titulo))
            return false;
        if (!TrimmedEqual(oldSheet.MercanciasResumen, newSheet.MercanciasResumen))
            return false;
        if (!TrimmedEqual(oldSheet.NotasGenerales, newSheet.NotasGenerales))
            return false;
        if (!TrimmedEqual(oldSheet.MonedaPago, newSheet.MonedaPago))
            return false;

        return TrimmedEqual(oldSheet.Estado, newSheet.Estado, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CarrierContactFieldsEqual(RouteStopPayload oldP, RouteStopPayload newP)
    {
        if (!TrimmedEqual(oldP.TelefonoTransportista, newP.TelefonoTransportista))
            return false;
        if (!TrimmedEqual(oldP.TransportInvitedStoreServiceId, newP.TransportInvitedStoreServiceId))
            return false;

        return TrimmedEqual(oldP.TransportInvitedServiceSummary, newP.TransportInvitedServiceSummary);
    }

    private static bool TrimmedEqual(
        string? left,
        string? right,
        StringComparison comparison = StringComparison.Ordinal) =>
        string.Equals((left ?? "").Trim(), (right ?? "").Trim(), comparison);
}
