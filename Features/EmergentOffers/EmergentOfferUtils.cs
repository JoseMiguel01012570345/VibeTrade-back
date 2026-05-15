using System.Text.Json;
using VibeTrade.Backend.Features.Offers;

namespace VibeTrade.Backend.Features.EmergentOffers;

public static class EmergentOfferUtils
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool TryNormalizeEmergentOfferId(string? emergentOfferId, out string normalized)
    {
        normalized = (emergentOfferId ?? "").Trim();
        if (normalized.Length < 4)
            return false;
        return OfferUtils.IsEmergentPublicationId(normalized);
    }

    /// <summary>
    /// Mismo criterio de snapshot en request: prefiero <c>PhoneDisplay</c> si existe; si no, uso <c>PhoneDigits</c>.
    /// Se trunca a 40 y se devuelve <c>null</c> si queda vacío.
    /// </summary>
    public static string? NormalizePhoneSnapshot(string? phoneDisplay, string? phoneDigits)
    {
        var snap = (phoneDisplay ?? "").Trim();
        if (snap.Length == 0 && !string.IsNullOrWhiteSpace(phoneDigits))
            snap = phoneDigits.Trim();
        if (snap.Length > 40)
            snap = snap[..40];
        return snap.Length > 0 ? snap : null;
    }

    public static string BuildRouteTramoSubscribeMetaJson(string routeSheetId, string stopId, string carrierUserId)
    {
        var meta = new RouteTramoSubscribeMeta(routeSheetId, stopId, carrierUserId);
        return JsonSerializer.Serialize(meta, MetaJsonOptions);
    }

    private sealed record RouteTramoSubscribeMeta(string RouteSheetId, string StopId, string CarrierUserId);
}

