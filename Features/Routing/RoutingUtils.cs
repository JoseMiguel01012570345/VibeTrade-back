using System.Globalization;
using VibeTrade.Backend.Features.RouteSheets.Dtos;
using VibeTrade.Backend.Features.Routing.Dtos;

namespace VibeTrade.Backend.Features.Routing;

public static class RoutingUtils
{
    public const int DefaultMaxLatLngPointsPerLeg = 4000;

    private const double LatLngEpsilon = 1e-8;

    public static bool TryParseCoordinate(string? latRaw, string? lngRaw, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var ls = (latRaw ?? "").Trim();
        var gs = (lngRaw ?? "").Trim();
        if (ls.Length == 0 || gs.Length == 0)
            return false;
        return double.TryParse(ls, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(gs, CultureInfo.InvariantCulture, out lng)
            && lat is >= -90 and <= 90
            && lng is >= -180 and <= 180;
    }

    public static string FormatInvariant(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static void ClearOsrmFields(RouteSheetPayload payload)
    {
        payload.Paradas ??= new List<RouteStopPayload>();
        foreach (var p in payload.Paradas)
        {
            p.OsrmRoadKm = null;
            p.OsrmRouteLatLngs = null;
        }
    }

    public static bool TryBuildPositionsForTramoChain(
        IReadOnlyList<RouteStopPayload> paradas,
        IReadOnlyList<int> chain,
        out List<(double Lat, double Lng)> positions)
    {
        positions = [];
        if (chain.Count == 0)
            return false;

        var first = paradas[chain[0]];
        if (!TryParseCoordinate(first.OrigenLat, first.OrigenLng, out var oLat, out var oLng))
            return false;
        positions.Add((oLat, oLng));

        foreach (var idx in chain)
        {
            var stop = paradas[idx];
            if (!TryParseCoordinate(stop.DestinoLat, stop.DestinoLng, out var dLat, out var dLng))
            {
                positions = [];
                return false;
            }
            positions.Add((dLat, dLng));
        }

        return positions.Count >= 2;
    }

    public static string BuildGraphHopperRouteQuery(
        (double Lat, double Lng) from,
        (double Lat, double Lng) to,
        string profile,
        string apiKey)
    {
        var q =
            $"route?type=json&points_encoded=false&profile={Uri.EscapeDataString(profile)}"
            + $"&point={FormatInvariant(from.Lat)},{FormatInvariant(from.Lng)}"
            + $"&point={FormatInvariant(to.Lat)},{FormatInvariant(to.Lng)}";
        if (apiKey.Length > 0)
            q += $"&key={Uri.EscapeDataString(apiKey)}";
        return q;
    }

    internal static (double DistanceKm, List<List<double>> Coordinates)? ParseGraphHopperLegPath(GhRouteEnvelope? data)
    {
        if (data?.Paths is null || data.Paths.Count == 0)
            return null;
        var path = data.Paths[0];
        var km = path.Distance / 1000d;
        var coords = path.Points?.Coordinates;
        if (coords is null || coords.Count < 2)
            return null;

        var latLng = new List<List<double>>();
        foreach (var c in coords)
        {
            if (c.Count < 2)
                continue;
            var lng = c[0];
            var lat = c[1];
            var pair = new List<double> { lat, lng };
            if (latLng.Count > 0 && SameLatLng(latLng[^1], pair))
                continue;
            latLng.Add(pair);
        }

        return latLng.Count >= 2 ? (km, latLng) : null;
    }

    public static List<List<double>> AppendPolylineSegmentsDedupe(
        IReadOnlyList<List<List<double>>> segments)
    {
        var merged = new List<List<double>>();
        foreach (var seg in segments)
        {
            if (seg is not { Count: >= 2 })
                continue;
            foreach (var pt in seg)
            {
                if (pt.Count < 2)
                    continue;
                if (merged.Count > 0 && SameLatLng(merged[^1], pt))
                    continue;
                merged.Add(new List<double> { pt[0], pt[1] });
            }
        }

        return merged.Count >= 2 ? merged : new List<List<double>>();
    }

    public static bool SameLatLng(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 2 || b.Count < 2)
            return false;
        return Math.Abs(a[0] - b[0]) < LatLngEpsilon && Math.Abs(a[1] - b[1]) < LatLngEpsilon;
    }

    public static List<List<double>> TrimPolylineDuplicateJoin(
        IReadOnlyList<double>? prevLegLastPoint,
        List<List<double>> legCoords)
    {
        if (legCoords.Count < 2 || prevLegLastPoint is not { Count: >= 2 })
            return legCoords;
        if (!SameLatLng(prevLegLastPoint, legCoords[0]))
            return legCoords;
        var tail = legCoords.Skip(1).Select(p => new List<double> { p[0], p[1] }).ToList();
        return tail.Count >= 2 ? tail : legCoords;
    }

    public static List<List<double>> SubsamplePolylineIfNeeded(List<List<double>> pts, int maxPoints)
    {
        if (pts.Count <= maxPoints)
            return pts;
        var stride = (int)Math.Ceiling((double)pts.Count / maxPoints);
        var slim = new List<List<double>>();
        for (var i = 0; i < pts.Count; i += stride)
            slim.Add(pts[i]);
        var last = pts[^1];
        if (!SameLatLng(slim[^1], last))
            slim.Add(last);
        return slim;
    }
}
