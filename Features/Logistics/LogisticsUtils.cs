using System.Globalization;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Titularidad operativa del tramo: solo un eslabón activo en cadena (pago → tramo 1; luego promoción / ceder).
/// </summary>
public static class LogisticsUtils
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
            or RouteStopDeliveryStates.AwaitingCarrierForHandoff
            or RouteStopDeliveryStates.InTransit
            or RouteStopDeliveryStates.DeliveredPendingEvidence
            or RouteStopDeliveryStates.EvidenceSubmitted
            or RouteStopDeliveryStates.EvidenceAccepted
            or RouteStopDeliveryStates.EvidenceRejected;
    }

    /// <summary>Índice del tramo en la hoja o -1.</summary>
    public static int StopIndex(IReadOnlyList<string> ordered, string stopId) =>
        IndexOfStop(ordered, stopId);

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lng2 - lng1) * Math.PI / 180;
        var rLat1 = lat1 * Math.PI / 180;
        var rLat2 = lat2 * Math.PI / 180;
        var h =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(rLat1) * Math.Cos(rLat2) * (Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return R * c;
    }

    public static bool TryParseLatLng(string? latRaw, string? lngRaw, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var lt = (latRaw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal);
        var lg = (lngRaw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(lt, CultureInfo.InvariantCulture, out lat)
               && double.TryParse(lg, CultureInfo.InvariantCulture, out lng);
    }

    public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    public static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

    public static (double DistanceMeters, double T01) DistancePointToSegmentMeters(
        double lat,
        double lng,
        (double Lat, double Lng) a,
        (double Lat, double Lng) b)
    {
        var ax = ToXMeters(a.Lng, lat);
        var ay = ToYMeters(a.Lat);
        var bx = ToXMeters(b.Lng, lat);
        var by = ToYMeters(b.Lat);
        var px = ToXMeters(lng, lat);
        var py = ToYMeters(lat);

        var abx = bx - ax;
        var aby = by - ay;
        var apx = px - ax;
        var apy = py - ay;
        var ab2 = abx * abx + aby * aby;
        if (ab2 <= 1e-12)
        {
            var d = Math.Sqrt(apx * apx + apy * apy);
            return (d, 0);
        }

        var t = (apx * abx + apy * aby) / ab2;
        t = Clamp(t, 0, 1);
        var cx = ax + t * abx;
        var cy = ay + t * aby;
        var dx = px - cx;
        var dy = py - cy;
        return (Math.Sqrt(dx * dx + dy * dy), t);
    }

    public static double ToXMeters(double lngDeg, double refLatDeg) =>
        (lngDeg * Math.PI / 180) * Math.Cos(refLatDeg * Math.PI / 180) * 6371000.0;

    public static double ToYMeters(double latDeg) =>
        (latDeg * Math.PI / 180) * 6371000.0;
}
