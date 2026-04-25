namespace VibeTrade.Backend.Features.Chat.Utils;

internal static class RouteTramoSubscriptionInputNormalize
{
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
}
