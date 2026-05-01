namespace VibeTrade.Backend.Features.Routing;

/// <summary>Concatena tramos y elimina puntos repetidos en los empalmes.</summary>
public static class DrivingPolylineMerge
{
    private const double Epsilon = 1e-8;

    /// <summary>
    /// Anexa coordenadas de cada tramo en orden; si el primer punto de un tramo coincide con el último del anterior, no se duplica.
    /// </summary>
    public static List<List<double>> AppendSegmentsDedupe(
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
        return Math.Abs(a[0] - b[0]) < Epsilon && Math.Abs(a[1] - b[1]) < Epsilon;
    }

    /// <summary>Quita el primer punto del tramo si coincide con el último del tramo previo (empalme cadena).</summary>
    public static List<List<double>> TrimDuplicateJoin(
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

    public static List<List<double>> SubsampleIfNeeded(List<List<double>> pts, int maxPoints)
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
