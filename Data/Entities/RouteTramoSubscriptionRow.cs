namespace VibeTrade.Backend.Data.Entities;

/// <summary>
/// Solicitud de suscripción a un tramo de una hoja publicada (persistida al registrar en servidor, p. ej. POST emergente).
/// </summary>
public sealed class RouteTramoSubscriptionRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string StopId { get; set; } = "";

    /// <summary>Orden del tramo en la hoja al momento de la solicitud (denormalizado).</summary>
    public int StopOrden { get; set; }

    public string CarrierUserId { get; set; } = "";

    /// <summary>Teléfono de contacto del transportista al momento de la solicitud (perfil / cuenta). Sirve para grabar <c>TelefonoTransportista</c> en la hoja al confirmar.</summary>
    public string? CarrierPhoneSnapshot { get; set; }

    public string? StoreServiceId { get; set; }

    /// <summary>Etiqueta del servicio (tipo · categoría) al momento de la solicitud.</summary>
    public string TransportServiceLabel { get; set; } = "";

    /// <summary><c>pending</c>, <c>confirmed</c>, <c>rejected</c> (actualización futura vía API; hoy suele inferirse desde la hoja).</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Huella del contenido del tramo (misma serialización que en la edición de hoja) al registrar, confirmar o tras notificar presel; evita re-notificar si el tramo aceptado no cambió.</summary>
    public string? StopContentFingerprint { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
