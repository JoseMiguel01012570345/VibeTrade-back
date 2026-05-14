namespace VibeTrade.Backend.Data.RouteSheets;

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
