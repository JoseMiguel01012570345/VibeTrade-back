using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteSheets;

public static class RouteSheetUtils
{
    /// <summary>Mismo patrón que <c>ROUTE_ESTIMADO_ISO_LOCAL_RE</c> en el front: <c>YYYY-MM-DDTHH:mm</c>.</summary>
    private static readonly Regex EstimadoIsoLocal = new(
        @"^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})$",
        RegexOptions.Compiled);

    public static RouteSheetPayload ClonePayload(RouteSheetPayload source) =>
        JsonSerializer.Deserialize<RouteSheetPayload>(
            JsonSerializer.Serialize(source, RouteSheetJson.Options),
            RouteSheetJson.Options) ?? source;

    public static string TruncateTitle(string? titulo)
    {
        var t = (titulo ?? "").Trim();
        return t.Length <= 120 ? t : t[..120] + "…";
    }

    public static RouteStopPayload? FindParadaByStopId(RouteSheetPayload payload, string stopId)
    {
        var sid = (stopId ?? "").Trim();
        if (sid.Length == 0)
            return null;
        return payload.Paradas?
            .FirstOrDefault(p => string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
    }

    public static RouteStopPayload? TryResolveParadaForSubscription(
        RouteSheetPayload payload,
        RouteTramoSubscriptionRow sub)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        var stopId = (sub.StopId ?? "").Trim();
        if (stopId.Length > 0)
        {
            var byId = FindParadaByStopId(payload, stopId);
            if (byId is not null)
                return byId;
        }

        if (sub.StopOrden > 0)
            return payload.Paradas.FirstOrDefault(p => p.Orden == sub.StopOrden);
        return null;
    }

    /// <summary>Rechazo/retiro: el vendedor puede volver a notificar aunque el tramo ya no tenga el teléfono en la hoja.</summary>
    public static bool PreselInviteEligibleAfterSheetPhoneCleared(string? status)
    {
        var st = (status ?? "").Trim().ToLowerInvariant();
        return st is "rejected" or "withdrawn";
    }

    /// <summary>
    /// <c>true</c> si este tramo ya está cubierto (suscripción pending/confirmed + mismo fingerprint) y no hace falta otro aviso presel.
    /// </summary>
    public static bool ShouldSkipPreselectedNotifyForSingleStop(
        RouteSheetPayload payload,
        string recipientUserId,
        string stopId,
        IReadOnlyList<RouteTramoSubscriptionRow> subsForSheet)
    {
        var parada = FindParadaByStopId(payload, stopId);
        if (parada is null)
            return false;

        var sid = (stopId ?? "").Trim();
        var fp = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
        var sub = subsForSheet.FirstOrDefault(s =>
            string.Equals((s.StopId ?? "").Trim(), sid, StringComparison.Ordinal)
            && ChatThreadAccess.UserIdsMatchLoose(recipientUserId, s.CarrierUserId));
        if (sub is null)
            return false;

        var st = (sub.Status ?? "").Trim().ToLowerInvariant();
        if (st is "rejected" or "withdrawn")
            return false;
        if (st is "pending" or "confirmed")
            return string.Equals(fp, sub.StopContentFingerprint ?? "", StringComparison.Ordinal);

        return false;
    }

    public static void ApplyStopContentFingerprintsAfterPreselectedNotify(
        RouteSheetPayload payload,
        List<RouteTramoSubscriptionRow> subsForSheet,
        string recipientUserId,
        IReadOnlyList<string> stopIdsForRecipient)
    {
        foreach (var stopId in stopIdsForRecipient)
        {
            var parada = FindParadaByStopId(payload, stopId);
            if (parada is null)
                continue;
            var fp = RouteSheetEditAckComputation.RouteStopFingerprint(parada);
            var sid = (stopId ?? "").Trim();
            foreach (var sub in subsForSheet)
            {
                if (!string.Equals((sub.StopId ?? "").Trim(), sid, StringComparison.Ordinal))
                    continue;
                if (!ChatThreadAccess.UserIdsMatchLoose(recipientUserId, sub.CarrierUserId))
                    continue;
                sub.StopContentFingerprint = fp;
            }
        }
    }

    public static string? ResolveCarrierKeyInEditAck(
        IReadOnlyDictionary<string, string> byCarrier,
        string viewerId)
    {
        var v = (viewerId ?? "").Trim();
        if (v.Length < 2)
            return null;
        foreach (var kv in byCarrier)
        {
            var k = (kv.Key ?? "").Trim();
            if (k.Length == 0)
                continue;
            if (string.Equals(k, v, StringComparison.Ordinal))
                return kv.Key;
            if (ChatThreadAccess.UserIdsMatchLoose(v, kv.Key))
                return kv.Key;
        }

        return null;
    }

    public static void PersistSheetPayloadWithAck(
        ChatRouteSheetRow row,
        RouteSheetEditAckPayload ack,
        DateTimeOffset updatedAt,
        Action<RouteSheetPayload>? mutateCloned = null)
    {
        var p = ClonePayload(row.Payload);
        mutateCloned?.Invoke(p);
        p.RouteSheetEditAck = ack;
        row.Payload = ClonePayload(p);
        row.UpdatedAtUtc = updatedAt;
    }

    public static void ApplyConfirmedSubscriptionStoreServicesToParadas(
        RouteSheetPayload payload,
        IReadOnlyList<RouteTramoSubscriptionRow> subs)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            if (!string.Equals((sub.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase))
                continue;
            var storeSvc = (sub.StoreServiceId ?? "").Trim();
            if (storeSvc.Length == 0)
                continue;
            var parada = TryResolveParadaForSubscription(payload, sub);
            if (parada is null)
                continue;
            var incomingInvited = (parada.TransportInvitedStoreServiceId ?? "").Trim();
            if (incomingInvited.Length > 0)
                continue;
            parada.TransportInvitedStoreServiceId = storeSvc;
            var label = (sub.TransportServiceLabel ?? "").Trim();
            if (label.Length > 0)
                parada.TransportInvitedServiceSummary = label;
        }
    }

    public static void ApplyParadaInvitedServicesToSubscriptions(
        RouteSheetPayload payload,
        List<RouteTramoSubscriptionRow> subs,
        DateTimeOffset now)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            var parada = TryResolveParadaForSubscription(payload, sub);
            if (parada is null)
                continue;
            var inv = (parada.TransportInvitedStoreServiceId ?? "").Trim();
            var sum = (parada.TransportInvitedServiceSummary ?? "").Trim();
            sub.StoreServiceId = inv.Length > 0 ? inv : null;
            sub.TransportServiceLabel = sum;
            sub.UpdatedAtUtc = now;
        }
    }

    public static void ClearTransportistaPhonesForSubs(
        RouteSheetPayload payload,
        IReadOnlyList<RouteTramoSubscriptionRow> subs)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var sub in subs)
        {
            var parada = TryResolveParadaForSubscription(payload, sub);
            if (parada is null)
                continue;
            parada.TelefonoTransportista = null;
            parada.TransportInvitedStoreServiceId = null;
            parada.TransportInvitedServiceSummary = null;
        }
    }

    /// <summary>
    /// Devuelve mensaje de error si los tiempos estimados encadenados son inválidos; <c>null</c> si está permitido.
    /// </summary>
    public static string? ValidateEstimatedTimes(RouteSheetPayload payload)
    {
        var paradas = payload.Paradas ?? [];
        var chains = BuildTramoChainsByCoords(paradas);
        foreach (var chain in chains)
        {
            for (var k = 0; k < chain.Count - 1; k++)
            {
                var a = chain[k];
                var b = chain[k + 1];
                if (!TryParseEstimadoIsoLocal(paradas[a].TiempoEntregaEstimado, out var entrega))
                    continue;
                if (!TryParseEstimadoIsoLocal(paradas[b].TiempoRecogidaEstimado, out var recogidaSiguiente))
                    continue;
                if (entrega > recogidaSiguiente)
                    return "La entrega estimada no puede ser posterior a la recogida estimada del tramo siguiente.";
            }
        }

        return null;
    }

    public static List<List<int>> BuildTramoChainsByCoords(IReadOnlyList<RouteStopPayload> paradas) =>
        RoutePathComputation.BuildTramoChainIndices(paradas);

    public static bool OrigenCoincideConDestinoAnterior(RouteStopPayload anterior, RouteStopPayload siguiente)
    {
        var dLat = (anterior.DestinoLat ?? "").Trim();
        var dLng = (anterior.DestinoLng ?? "").Trim();
        var oLat = (siguiente.OrigenLat ?? "").Trim();
        var oLng = (siguiente.OrigenLng ?? "").Trim();
        if (dLat.Length == 0 || dLng.Length == 0 || oLat.Length == 0 || oLng.Length == 0)
            return false;
        return string.Equals(dLat, oLat, StringComparison.Ordinal)
               && string.Equals(dLng, oLng, StringComparison.Ordinal);
    }

    public static bool TryParseEstimadoIsoLocal(string? raw, out DateTime local)
    {
        local = default;
        var t = (raw ?? "").Trim();
        var m = EstimadoIsoLocal.Match(t);
        if (!m.Success)
            return false;
        return DateTime.TryParseExact(
            $"{m.Groups[1].Value}T{m.Groups[2].Value}:00",
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out local);
    }
}
