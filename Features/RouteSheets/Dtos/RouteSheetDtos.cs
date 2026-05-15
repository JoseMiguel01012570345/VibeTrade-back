namespace VibeTrade.Backend.Features.RouteSheets.Dtos;

/// <summary>Tramo de ruta; mismo contrato que <c>RouteStop</c> en el cliente.</summary>
public sealed class RouteStopPayload
{
    public string Id { get; set; } = "";

    public int Orden { get; set; }

    public string Origen { get; set; } = "";

    public string Destino { get; set; } = "";

    public string? OrigenLat { get; set; }

    public string? OrigenLng { get; set; }

    public string? DestinoLat { get; set; }

    public string? DestinoLng { get; set; }

    public string? TiempoRecogidaEstimado { get; set; }

    public string? TiempoEntregaEstimado { get; set; }

    public string? PrecioTransportista { get; set; }

    public string? CargaEnTramo { get; set; }

    public string? TipoMercanciaCarga { get; set; }

    public string? TipoMercanciaDescarga { get; set; }

    public string? Notas { get; set; }

    public string? ResponsabilidadEmbalaje { get; set; }

    public string? RequisitosEspeciales { get; set; }

    public string? TipoVehiculoRequerido { get; set; }

    public string? TelefonoTransportista { get; set; }

    /// <summary>Servicio de vitrina (catálogo) con el que el vendedor invita al transportista en este tramo.</summary>
    public string? TransportInvitedStoreServiceId { get; set; }

    /// <summary>Resumen corto para UI y notificaciones (p. ej. tipo · categoría).</summary>
    public string? TransportInvitedServiceSummary { get; set; }

    public bool? Completada { get; set; }

    public string? Lugar { get; set; }

    public string? VentanaHoraria { get; set; }

    /// <summary>Moneda del precio de este tramo (ej. USD, CUP); mismo catálogo que <c>GET /market/currencies</c>.</summary>
    public string? MonedaPago { get; set; }

    /// <summary>Km por carretera (OSRM) en este tramo O→D; rellenado al persistir la hoja (una petición OSRM por cadena conexa).</summary>
    public double? OsrmRoadKm { get; set; }

    /// <summary>Puntos [lat, lng] de la polilínea por carretera (OSRM); rellenado al persistir la hoja.</summary>
    public List<List<double>>? OsrmRouteLatLngs { get; set; }
}

/// <summary>Hoja de ruta; mismo contrato que <c>RouteSheet</c> en el cliente (JSON camelCase).</summary>
public sealed class RouteSheetPayload
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string Titulo { get; set; } = "";

    public long CreadoEn { get; set; }

    public long ActualizadoEn { get; set; }

    public string Estado { get; set; } = "programada";

    public string MercanciasResumen { get; set; } = "";

    public List<RouteStopPayload> Paradas { get; set; } = new();

    public string? NotasGenerales { get; set; }

    /// <summary>Resumen opcional: si todos los tramos usan la misma moneda, un solo código; si no, códigos unidos. Por tramo: <see cref="RouteStopPayload.MonedaPago"/>.</summary>
    public string? MonedaPago { get; set; }

    public bool? PublicadaPlataforma { get; set; }

    public bool? EditadaEnFormulario { get; set; }

    /// <summary>Acuses de transportistas tras editar la hoja (servidor es fuente de verdad).</summary>
    public RouteSheetEditAckPayload? RouteSheetEditAck { get; set; }
}

/// <summary>Acuse post-edición de hoja (mismo contrato que <c>routeSheetEditAcks</c> en el cliente).</summary>
public sealed class RouteSheetEditAckPayload
{
    public int Revision { get; set; }

    /// <summary>userId transportista → pending | accepted | rejected</summary>
    public Dictionary<string, string> ByCarrier { get; set; } = new(StringComparer.Ordinal);
}

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

    /// <summary>Km por carretera en este tramo (misma fuente que la hoja persistida).</summary>
    public double? OsrmRoadKm { get; set; }

    /// <summary>Puntos [lat, lng] de la polilínea OSRM (si la hoja la persistió al guardar).</summary>
    public List<List<double>>? OsrmRouteLatLngs { get; set; }
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
