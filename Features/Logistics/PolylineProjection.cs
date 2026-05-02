namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Proyección de un punto GPS sobre una polilínea ([lat,lng], …) y avance normalizado 0..1.
/// </summary>
public static class PolylineProjection
{
    public readonly record struct ProjectionResult(
        double DistanceToPolylineMeters,
        double DistanceAlongMeters,
        double TotalLengthMeters,
        double Progress01,
        bool OffRoute);

    public static ProjectionResult ProjectToPolyline(
        double lat,
        double lng,
        IReadOnlyList<List<double>> latLngPoints,
        double offRouteToleranceMeters)
    {
        if (latLngPoints.Count < 2)
            return new ProjectionResult(double.NaN, 0, 0, 0, true);

        var pts = new List<(double Lat, double Lng)>(latLngPoints.Count);
        foreach (var p in latLngPoints)
        {
            if (p.Count < 2) continue;
            pts.Add((p[0], p[1]));
        }

        if (pts.Count < 2)
            return new ProjectionResult(double.NaN, 0, 0, 0, true);

        double bestDist = double.PositiveInfinity;
        var cumulative = new double[pts.Count];
        cumulative[0] = 0;
        for (var i = 1; i < pts.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + HaversineMeters(pts[i - 1], pts[i]);
        }

        var total = cumulative[^1];
        if (total <= 1e-6)
            return new ProjectionResult(0, 0, 0, 0, false);

        double alongBest = 0;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var segLen = cumulative[i] - cumulative[i - 1];
            if (segLen <= 1e-9)
                continue;

            var (d, t) = DistancePointToSegmentMeters(lat, lng, a, b);
            if (d < bestDist)
            {
                bestDist = d;
                alongBest = cumulative[i - 1] + t * segLen;
            }
        }

        var progress = Clamp01(alongBest / total);
        var off = bestDist > offRouteToleranceMeters;
        return new ProjectionResult(bestDist, alongBest, total, progress, off);
    }

    public static double AdaptiveToleranceMeters(double totalRouteMeters)
    {
        // Heurística: más tolerancia en tramos largos / autopista; mínimo urbano ~35m.
        if (double.IsNaN(totalRouteMeters) || totalRouteMeters <= 0)
            return 45;
        var baseTol = 25 + Math.Sqrt(totalRouteMeters) * 0.35;
        return Clamp(baseTol, 35, 220);
    }

    private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    private static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

    private static (double DistanceMeters, double T01) DistancePointToSegmentMeters(
        double lat,
        double lng,
        (double Lat, double Lng) a,
        (double Lat, double Lng) b)
    {
        // Aproximación plana local en metros alrededor del punto (suficiente para tolerancias ~50–200m).
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

    private static double ToXMeters(double lngDeg, double refLatDeg) =>
        (lngDeg * Math.PI / 180) * Math.Cos(refLatDeg * Math.PI / 180) * 6371000.0;

    private static double ToYMeters(double latDeg) =>
        (latDeg * Math.PI / 180) * 6371000.0;

    private static double HaversineMeters((double Lat, double Lng) a, (double Lat, double Lng) b)
    {
        const double R = 6371000.0;
        var dLat = (b.Lat - a.Lat) * Math.PI / 180;
        var dLon = (b.Lng - a.Lng) * Math.PI / 180;
        var lat1 = a.Lat * Math.PI / 180;
        var lat2 = b.Lat * Math.PI / 180;
        var h =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * (Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return R * c;
    }
}
