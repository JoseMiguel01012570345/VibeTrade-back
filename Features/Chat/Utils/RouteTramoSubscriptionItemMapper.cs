using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using RouteTramoItemDto = global::VibeTrade.Backend.Features.Chat.RouteTramoSubscriptionItemDto;

namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoSubscriptionItemMapper
{
    public static RouteTramoItemDto MapRow(
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
        var display = RouteTramoUserContactUtil.CarrierDisplayOrDefault(acc?.DisplayName);
        var phone = RouteTramoUserContactUtil.BestPhoneForCarrier(acc, r.CarrierPhoneSnapshot, parada);
        var trust = acc?.TrustScore ?? 0;
        var status = RouteTramoSubscriptionStatusUtil.Normalized(r.Status);
        var createdMs = r.CreatedAtUtc.ToUnixTimeMilliseconds();
        string? svcStore = null;
        if (!string.IsNullOrWhiteSpace(r.StoreServiceId)
            && serviceIdToStoreId.TryGetValue(r.StoreServiceId.Trim(), out var st))
            svcStore = st;

        var avatarUrl = string.IsNullOrWhiteSpace(acc?.AvatarUrl) ? null : acc.AvatarUrl.Trim();

        return new RouteTramoItemDto(
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
}
