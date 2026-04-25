using System.Linq;
using System.Text.Json;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Detecta transportistas afectados por edición de paradas (alineado al fingerprint del cliente).</summary>
public static class RouteSheetEditAckComputation
{
    /// <summary>Descuento a la tienda al eliminar hoja con transportistas confirmados (demo).</summary>
    public const int StoreTrustPenaltyPerConfirmedCarrierOnSheetDelete = 3;
    /// <summary>Descuento a la tienda cuando un transportista confirmado rechaza la edición de la hoja.</summary>
    public const int StoreTrustPenaltyOnCarrierRejectSheetEdit = 3;

    /// <summary>Descuento a la tienda al expulsar a un transportista ya confirmado (demo).</summary>
    public const int StoreTrustPenaltyOnSellerExpelConfirmedCarrier = 3;

    public static bool HasPendingCarrierAck(RouteSheetEditAckPayload? ack)
    {
        if (ack?.ByCarrier is null) return false;
        foreach (var v in ack.ByCarrier.Values)
        {
            if (string.Equals((v ?? "").Trim(), "pending", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string RouteStopFingerprint(RouteStopPayload p) =>
        JsonSerializer.Serialize(new
        {
            orden = p.Orden,
            origen = (p.Origen ?? "").Trim(),
            destino = (p.Destino ?? "").Trim(),
            olat = (p.OrigenLat ?? "").Trim(),
            olng = (p.OrigenLng ?? "").Trim(),
            dlat = (p.DestinoLat ?? "").Trim(),
            dlng = (p.DestinoLng ?? "").Trim(),
            t1 = (p.TiempoRecogidaEstimado ?? "").Trim(),
            t2 = (p.TiempoEntregaEstimado ?? "").Trim(),
            pr = (p.PrecioTransportista ?? "").Trim(),
            tel = (p.TelefonoTransportista ?? "").Trim(),
            cg = (p.CargaEnTramo ?? "").Trim(),
            tmc = (p.TipoMercanciaCarga ?? "").Trim(),
            tmd = (p.TipoMercanciaDescarga ?? "").Trim(),
            no = (p.Notas ?? "").Trim(),
            re = (p.ResponsabilidadEmbalaje ?? "").Trim(),
            rq = (p.RequisitosEspeciales ?? "").Trim(),
            ve = (p.TipoVehiculoRequerido ?? "").Trim(),
            mon = (p.MonedaPago ?? "").Trim(),
        });

    public static HashSet<string> ConfirmedCarrierIdsForSheet(
        List<RouteTramoSubscriptionRow> subs,
        string routeSheetId)
    {
        var rsid = (routeSheetId ?? "").Trim();
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in subs)
        {
            if (!string.Equals(s.RouteSheetId, rsid, StringComparison.Ordinal))
                continue;
            if (string.Equals((s.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                set.Add(s.CarrierUserId);
        }
        return set;
    }

    public static HashSet<string> AffectedConfirmedCarrierIds(
        RouteSheetPayload oldSheet,
        RouteSheetPayload newSheet,
        List<RouteTramoSubscriptionRow> subsForSheet)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var oldById = (oldSheet.Paradas ?? []).ToDictionary(x => (x.Id ?? "").Trim(), StringComparer.Ordinal);
        var newById = (newSheet.Paradas ?? []).ToDictionary(x => (x.Id ?? "").Trim(), StringComparer.Ordinal);

        foreach (var sub in subsForSheet)
        {
            if (!string.Equals((sub.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            var sid = (sub.StopId ?? "").Trim();
            RouteStopPayload? oldP = null;
            RouteStopPayload? newP = null;
            if (sid.Length > 0)
            {
                oldById.TryGetValue(sid, out oldP);
                newById.TryGetValue(sid, out newP);
            }
            if (oldP is null && newP is null && sub.StopOrden > 0)
            {
                oldP = (oldSheet.Paradas ?? []).FirstOrDefault(p => p.Orden == sub.StopOrden);
                newP = (newSheet.Paradas ?? []).FirstOrDefault(p => p.Orden == sub.StopOrden);
            }
            if (sid.Length == 0 && sub.StopOrden <= 0)
                continue;
            if (oldP is null || newP is null)
            {
                affected.Add(sub.CarrierUserId);
                continue;
            }
            if (!string.Equals(RouteStopFingerprint(oldP), RouteStopFingerprint(newP), StringComparison.Ordinal))
                affected.Add(sub.CarrierUserId);
        }

        return affected;
    }

    public static RouteSheetEditAckPayload? BuildNextEditAck(
        RouteSheetEditAckPayload? prevAck,
        HashSet<string> assignedConfirmedCarriers,
        HashSet<string> affectedCarriers)
    {
        if (assignedConfirmedCarriers.Count == 0 || affectedCarriers.Count == 0)
            return null;

        var prevBy = prevAck?.ByCarrier ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var prevRev = prevAck?.Revision ?? 0;
        var nextBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var uid in assignedConfirmedCarriers)
        {
            if (affectedCarriers.Contains(uid))
                nextBy[uid] = "pending";
            else
            {
                var prev = prevBy.TryGetValue(uid, out var p) ? p : null;
                nextBy[uid] = string.Equals((prev ?? "").Trim(), "pending", StringComparison.OrdinalIgnoreCase)
                    ? "pending"
                    : (string.IsNullOrWhiteSpace(prev) ? "accepted" : prev.Trim().ToLowerInvariant());
            }
        }

        return new RouteSheetEditAckPayload { Revision = prevRev + 1, ByCarrier = nextBy };
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
