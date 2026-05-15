using System.Diagnostics.CodeAnalysis;
using System.Linq;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Notifications;

/// <summary>Composición de textos de avisos de sistema para ediciones de hoja de ruta con acuse de transportistas.</summary>
public static class RouteSheetEditAckNoticeComposer
{
    /// <summary>
    /// Tras guardar: mismas paradas por id y mismos datos de tramo; algún tramo con transportista confirmado pasó a otra posición (p. ej. inserción).
    /// </summary>
    public static bool TryBuildTramoRenumberSystemNotice(
        RouteSheetPayload? oldSheet,
        RouteSheetPayload newSheet,
        IReadOnlyList<RouteTramoSubscriptionRow> confirmedSubsOnSheet,
        [NotNullWhen(true)] out string? notice)
    {
        notice = null;
        if (oldSheet is null)
            return false;

        var oldList = oldSheet.Paradas ?? [];
        var newList = newSheet.Paradas ?? [];
        var oldById = oldList
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id.Trim(), StringComparer.Ordinal);
        var newById = newList
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id.Trim(), StringComparer.Ordinal);

        foreach (var kv in oldById)
        {
            if (!newById.TryGetValue(kv.Key, out var newP))
                return false;
            if (!string.Equals(
                    RouteSheetEditAckComputation.RouteStopFingerprintExcludingPhone(kv.Value),
                    RouteSheetEditAckComputation.RouteStopFingerprintExcludingPhone(newP),
                    StringComparison.Ordinal))
                return false;
        }

        var anyConfirmedMoved = false;
        foreach (var sub in confirmedSubsOnSheet)
        {
            if (!string.Equals((sub.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            var sid = (sub.StopId ?? "").Trim();
            if (sid.Length == 0)
                continue;
            if (!oldById.ContainsKey(sid) || !newById.ContainsKey(sid))
                continue;
            var io = oldList.FindIndex(x => string.Equals((x.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            var ni = newList.FindIndex(x => string.Equals((x.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (io >= 0 && ni >= 0 && io != ni)
                anyConfirmedMoved = true;
        }

        if (!anyConfirmedMoved)
            return false;

        notice =
            "La numeración de tramos en la hoja cambió (por ejemplo, al insertar un tramo). "
            + "Los transportistas confirmados conservan el mismo recorrido asignado; solo cambia el número de tramo que ven en la lista.";
        return true;
    }

    public static string BuildEditNoticeText(
        string titulo,
        RouteSheetPayload newSheet,
        HashSet<string> affectedCarrierIds,
        List<RouteTramoSubscriptionRow> confirmedSubsOnSheet,
        IReadOnlyDictionary<string, string> carrierDisplayNames)
    {
        var newById = (newSheet.Paradas ?? []).ToDictionary(x => (x.Id ?? "").Trim(), StringComparer.Ordinal);
        var detailMap = new Dictionary<string, (string Name, List<string> Tramos)>(StringComparer.Ordinal);

        foreach (var sub in confirmedSubsOnSheet)
        {
            if (!string.Equals((sub.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!affectedCarrierIds.Contains(sub.CarrierUserId))
                continue;

            if (!detailMap.TryGetValue(sub.CarrierUserId, out var entry))
            {
                var nm = carrierDisplayNames.TryGetValue(sub.CarrierUserId, out var dn) && !string.IsNullOrWhiteSpace(dn)
                    ? dn.Trim()
                    : "Transportista";
                entry = (nm, new List<string>());
                detailMap[sub.CarrierUserId] = entry;
            }

            var sid = (sub.StopId ?? "").Trim();
            if (!newById.TryGetValue(sid, out var newP))
                entry.Tramos.Add($"Tramo eliminado o reasignado (orden {sub.StopOrden})");
            else
                entry.Tramos.Add($"Tramo {newP.Orden} ({newP.Origen} → {newP.Destino})");
        }

        var t = (titulo ?? "").Trim();
        if (t.Length > 120)
            t = t[..120] + "…";
        if (detailMap.Count == 0)
            return t.Length > 0 ? $"Hoja de ruta «{t}» editada." : "Hoja de ruta editada.";

        var parts = new List<string>();
        foreach (var v in detailMap.Values)
        {
            var tlist = v.Tramos.Count > 0 ? string.Join(", ", v.Tramos) : "tramo asignado";
            parts.Add($"{v.Name} ({tlist})");
        }

        if (parts.Count == 1)
            return $"Hoja de ruta «{t}» editada. Solo {parts[0]} puede aceptar o rechazar esta versión en la pestaña Rutas (solo el transportista de ese tramo).";
        return $"Hoja de ruta «{t}» editada. {string.Join("; ", parts)}: cada transportista solo puede aceptar o rechazar los cambios de su propio tramo en la pestaña Rutas.";
    }
}
