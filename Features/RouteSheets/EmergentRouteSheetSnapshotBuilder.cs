namespace VibeTrade.Backend.Features.RouteSheets;

/// <summary>Construye snapshots emergentes desde hojas de ruta persistidas.</summary>
public static class EmergentRouteSheetSnapshotBuilder
{
    public static EmergentRouteSheetSnapshot FromRouteSheet(RouteSheetPayload sheet)
    {
        var paradas = (sheet.Paradas ?? [])
            .Select(p => new EmergentRouteLegSnapshot
            {
                StopId = (p.Id ?? "").Trim(),
                Orden = p.Orden,
                Origen = p.Origen ?? "",
                Destino = p.Destino ?? "",
                OrigenLat = p.OrigenLat,
                OrigenLng = p.OrigenLng,
                DestinoLat = p.DestinoLat,
                DestinoLng = p.DestinoLng,
                MonedaPago = p.MonedaPago?.Trim() ?? "",
                PrecioTransportista = p.PrecioTransportista?.Trim() ?? "",
                OsrmRoadKm = p.OsrmRoadKm,
                OsrmRouteLatLngs = p.OsrmRouteLatLngs is { Count: >= 2 } ? p.OsrmRouteLatLngs : null,
            })
            .ToList();
        return new EmergentRouteSheetSnapshot
        {
            Titulo = sheet.Titulo ?? "",
            MercanciasResumen = sheet.MercanciasResumen ?? "",
            MonedaPago = SummarizeMonedaPago(sheet, paradas),
            Paradas = paradas,
        };
    }

    private static string SummarizeMonedaPago(RouteSheetPayload sheet, IReadOnlyList<EmergentRouteLegSnapshot> paradas)
    {
        var fromStops = paradas
            .Select(leg => leg.MonedaPago.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (fromStops.Count == 0)
            return (sheet.MonedaPago ?? "").Trim();
        var distinct = fromStops.Distinct().ToList();
        if (distinct.Count == 1) return distinct[0];
        return string.Join(" · ", distinct);
    }
}
