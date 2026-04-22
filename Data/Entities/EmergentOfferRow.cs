using VibeTrade.Backend.Data.RouteSheets;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>
/// Oferta emergente (p. ej. hoja de ruta publicada) usada por el algoritmo de recomendación
/// para priorizar ciertas ofertas de catálogo ante perfiles elegibles (transportistas).
/// </summary>
public sealed class EmergentOfferRow
{
    public string Id { get; set; } = "";

    /// <summary>Por ahora <c>route_sheet</c>; extensible a otros tipos.</summary>
    public string Kind { get; set; } = "";

    public string ThreadId { get; set; } = "";

    /// <summary>Id de producto/servicio del hilo (oferta de marketplace).</summary>
    public string OfferId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string PublisherUserId { get; set; } = "";

    public EmergentRouteSheetSnapshot RouteSheetSnapshot { get; set; } = new();

    public DateTimeOffset PublishedAtUtc { get; set; }

    /// <summary>Null = señal activa para recomendaciones.</summary>
    public DateTimeOffset? RetractedAtUtc { get; set; }
}
