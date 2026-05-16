using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions;

public static class SubscriptionsUtils
{
    /// <summary>Texto en hoja / suscripción cuando la invitación presel no liga una ficha de catálogo.</summary>
    public const string PreselDefaultTransportServiceLabel = "Servicio de transporte";

    /// <summary>Marca en la hoja: el teléfono del tramo proviene de la invitación por contacto en la hoja.</summary>
    public const string PreselSheetContactNoteMarker = "Contacto indicado en la hoja";

    public static (string Tid, string Rsid, string Sid, string Uid) TrimTramoRequestKeys(
        string threadId,
        string routeSheetId,
        string stopId,
        string carrierUserId) => (
        (threadId ?? "").Trim(),
        (routeSheetId ?? "").Trim(),
        (stopId ?? "").Trim(),
        (carrierUserId ?? "").Trim());

    public static (string? Svc, string Label, string? PhoneSnap) NormalizeOptionalFields(
        string? storeServiceId,
        string transportServiceLabel,
        string? carrierContactPhone)
    {
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

        return (svcTrim, label, snap);
    }

    public static string PhoneSnapForCarrier(UserAccount carrier)
    {
        var phoneSnap = (carrier.PhoneDisplay ?? "").Trim();
        if (phoneSnap.Length == 0 && !string.IsNullOrWhiteSpace(carrier.PhoneDigits))
            phoneSnap = carrier.PhoneDigits.Trim();
        if (phoneSnap.Length > 40)
            phoneSnap = phoneSnap[..40];
        return phoneSnap;
    }

    public static List<RouteStopPayload> PreselMatchStops(
        RouteSheetPayload payload,
        string carrierDigits,
        string? stopIdRestrict)
    {
        var stopRestrict = (stopIdRestrict ?? "").Trim();
        var list = new List<RouteStopPayload>();
        foreach (var p in payload.Paradas ?? [])
        {
            var d = AuthUtils.DigitsOnly(p.TelefonoTransportista);
            if (d.Length < 6 || !string.Equals(d, carrierDigits, StringComparison.Ordinal))
                continue;
            if (stopRestrict.Length > 0
                && !string.Equals((p.Id ?? "").Trim(), stopRestrict, StringComparison.Ordinal))
                continue;
            list.Add(p);
        }
        return list;
    }

    public static void ApplyPreselAcceptedFieldsToParada(
        RouteStopPayload parada,
        string? invitedStoreServiceId,
        string transportServiceLabel)
    {
        parada.TransportInvitedServiceSummary = transportServiceLabel;
        if (string.IsNullOrWhiteSpace(invitedStoreServiceId))
            parada.TransportInvitedStoreServiceId = null;

        var notas = (parada.Notas ?? "").Trim();
        if (notas.Contains(PreselSheetContactNoteMarker, StringComparison.OrdinalIgnoreCase))
            return;
        parada.Notas = notas.Length == 0
            ? PreselSheetContactNoteMarker
            : $"{PreselSheetContactNoteMarker}. {notas}";
    }

    public static void MarkSubscriptionsWithdrawn(
        List<RouteTramoSubscriptionRow> subs,
        DateTimeOffset now)
    {
        foreach (var s in subs)
        {
            s.Status = "withdrawn";
            s.UpdatedAtUtc = now;
        }
    }

    public static RouteStopPayload? FindParadaByStopIdOrOrden(
        IReadOnlyList<RouteStopPayload> paradas,
        string? stopId,
        int stopOrden)
    {
        var sid = (stopId ?? "").Trim();
        if (sid.Length > 0)
        {
            var byId = paradas.FirstOrDefault(p =>
                string.Equals((p.Id ?? "").Trim(), sid, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }
        if (stopOrden > 0)
            return paradas.FirstOrDefault(p => p.Orden == stopOrden);
        return null;
    }

    public static string NormalizedStatus(string? status) =>
        (status ?? "pending").Trim().ToLowerInvariant();

    public static bool IsPendingForSellerDecision(RouteTramoSubscriptionRow r) =>
        NormalizedStatus(r.Status) is not ("confirmed" or "rejected" or "withdrawn");

    public static bool AllowsCarrierThreadParticipation(string? status) =>
        NormalizedStatus(status) is not ("rejected" or "withdrawn");

    public static string BestPhoneForCarrier(
        UserAccount? acc,
        string? subSnapshot,
        RouteStopPayload? parada)
    {
        var phone = (acc?.PhoneDisplay ?? "").Trim();
        if (phone.Length == 0 && !string.IsNullOrWhiteSpace(acc?.PhoneDigits))
            phone = acc!.PhoneDigits!.Trim();
        if (phone.Length == 0)
            phone = (subSnapshot ?? "").Trim();
        if (phone.Length == 0 && parada is not null)
            phone = (parada.TelefonoTransportista ?? "").Trim();
        return phone;
    }

    public static string CarrierDisplayOrDefault(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Transportista" : displayName.Trim();

    public static string ParticipanteOrDisplay(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "Participante" : displayName!.Trim();

    public static RouteTramoSubscriptionItemDto MapSubscriptionItem(
        RouteTramoSubscriptionRow r,
        RouteSheetPayload? payload,
        IReadOnlyDictionary<string, UserAccount> accounts,
        IReadOnlyDictionary<string, string> serviceIdToStoreId)
    {
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
        var display = CarrierDisplayOrDefault(acc?.DisplayName);
        var phone = BestPhoneForCarrier(acc, r.CarrierPhoneSnapshot, parada);
        var trust = acc?.TrustScore ?? 0;
        var status = NormalizedStatus(r.Status);
        var createdMs = r.CreatedAtUtc.ToUnixTimeMilliseconds();
        string? svcStore = null;
        if (!string.IsNullOrWhiteSpace(r.StoreServiceId)
            && serviceIdToStoreId.TryGetValue(r.StoreServiceId.Trim(), out var st))
            svcStore = st;

        var avatarUrl = string.IsNullOrWhiteSpace(acc?.AvatarUrl) ? null : acc.AvatarUrl.Trim();

        return new RouteTramoSubscriptionItemDto(
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
            svcStore,
            avatarUrl);
    }

    public static List<RouteTramoSubscriptionItemDto> NarrowItemsForCarrierViewer(
        string viewerUserId,
        List<RouteTramoSubscriptionItemDto> items)
    {
        var v = (viewerUserId ?? "").Trim();
        if (v.Length < 2)
            return [];
        return items
            .Where(dto =>
                string.Equals((dto.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase)
                || ChatThreadAccess.UserIdsMatchLoose(v, dto.CarrierUserId))
            .ToList();
    }

    public static (string Label, int Trust) SellerLabelAndTrust(StoreRow? store, UserAccount? actorAcc)
    {
        var label = !string.IsNullOrWhiteSpace(store?.Name) ? store!.Name.Trim()
            : string.IsNullOrWhiteSpace(actorAcc?.DisplayName) ? "Vendedor"
            : actorAcc!.DisplayName.Trim();
        var trust = store?.TrustScore ?? actorAcc?.TrustScore ?? 0;
        return (label, trust);
    }

    public static string BuildWithdrawAutomatedNotice(
        string whoDisplay,
        int nTramos,
        int nSheets,
        bool applyTrustPenalty,
        bool noPenaltyBecauseSheetsDelivered = false,
        string? withdrawReason = null)
    {
        var who = (whoDisplay ?? "").Trim();
        if (who.Length == 0)
            who = "Un transportista";
        if (who.Length > 120)
            who = who[..120] + "…";
        var sys = nSheets <= 1
            ? $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s))."
            : $"{who} dejó de participar como transportista en este hilo (se retiró de {nTramos} tramo(s) en {nSheets} hojas de ruta).";
        if (applyTrustPenalty)
            sys += " Se aplicó un ajuste de confianza por retirarse como transportista con tramos confirmados (demo).";
        else if (noPenaltyBecauseSheetsDelivered)
            sys += " No aplica ajuste de confianza: los tramos confirmados estaban cerrados (hoja entregada o logística liquidada/vencida).";
        var r = (withdrawReason ?? "").Trim();
        if (r.Length > 0)
        {
            if (r.Length > 400)
                r = r[..400] + "…";
            sys += $" Motivo declarado: {r}";
        }

        return sys;
    }

    public static bool IsCarrierLegTrustTerminalState(string? stateRaw)
    {
        var s = (stateRaw ?? "").Trim().ToLowerInvariant();
        return s is RouteStopDeliveryStates.EvidenceAccepted
            || RouteStopDeliveryStates.IsRefundedTerminal(s)
            || s == RouteStopDeliveryStates.IdleStoreCustody;
    }
}
