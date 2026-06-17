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

    private static bool SheetHeaderUnchanged(RouteSheetPayload oldSheet, RouteSheetPayload newSheet) =>
        string.Equals((oldSheet.Titulo ?? "").Trim(), (newSheet.Titulo ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldSheet.MercanciasResumen ?? "").Trim(), (newSheet.MercanciasResumen ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldSheet.NotasGenerales ?? "").Trim(), (newSheet.NotasGenerales ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldSheet.MonedaPago ?? "").Trim(), (newSheet.MonedaPago ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldSheet.Estado ?? "").Trim(), (newSheet.Estado ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        && oldSheet.PublicadaPlataforma == newSheet.PublicadaPlataforma;

    private static bool CarrierContactFieldsEqual(RouteStopPayload oldP, RouteStopPayload newP) =>
        string.Equals((oldP.TelefonoTransportista ?? "").Trim(), (newP.TelefonoTransportista ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldP.TransportInvitedStoreServiceId ?? "").Trim(), (newP.TransportInvitedStoreServiceId ?? "").Trim(), StringComparison.Ordinal)
        && string.Equals((oldP.TransportInvitedServiceSummary ?? "").Trim(), (newP.TransportInvitedServiceSummary ?? "").Trim(), StringComparison.Ordinal);
}
