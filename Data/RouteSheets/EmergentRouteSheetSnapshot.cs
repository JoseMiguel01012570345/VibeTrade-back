namespace VibeTrade.Backend.Data.RouteSheets;

/// <summary>Resumen tipado de una hoja publicada para señales de recomendación (sin duplicar el payload completo).</summary>
public sealed class EmergentRouteLegSnapshot
{
    /// <summary>Coincide con <see cref="RouteStopPayload.Id"/> en la hoja persistida (validación de suscripción a tramo).</summary>
    public string StopId { get; set; } = "";

    /// <summary>Orden del tramo en la hoja (1-based típico); necesario para rehidratar <see cref="StopId"/> desde la hoja viva si el snapshot es viejo.</summary>
    public int Orden { get; set; }

    public string Origen { get; set; } = "";

    public string Destino { get; set; } = "";

    public string? OrigenLat { get; set; }

    public string? OrigenLng { get; set; }

    public string? DestinoLat { get; set; }

    public string? DestinoLng { get; set; }

    public string MonedaPago { get; set; } = "";

    /// <summary>Precio / tarifa del transportista en este tramo (texto libre en la hoja).</summary>
    public string PrecioTransportista { get; set; } = "";
}

public sealed class EmergentRouteSheetSnapshot
{
    public string Titulo { get; set; } = "";

    public string MercanciasResumen { get; set; } = "";

    /// <summary>Moneda de pago (logística) indicada en la hoja al publicar.</summary>
    public string MonedaPago { get; set; } = "";

    public List<EmergentRouteLegSnapshot> Paradas { get; set; } = new();

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
