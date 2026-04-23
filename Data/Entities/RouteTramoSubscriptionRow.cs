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

    public string? StoreServiceId { get; set; }

    /// <summary>Etiqueta del servicio (tipo · categoría) al momento de la solicitud.</summary>
    public string TransportServiceLabel { get; set; } = "";

    /// <summary><c>pending</c>, <c>confirmed</c>, <c>rejected</c> (actualización futura vía API; hoy suele inferirse desde la hoja).</summary>
    public string Status { get; set; } = "pending";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
