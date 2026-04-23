using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;

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

    /// <summary>Id del producto/servicio en el hilo (contexto de mercado). La publicación emergente se identifica por <see cref="Id" />; no es el id de la hoja de ruta.</summary>
    public string OfferId { get; set; } = "";

    public string RouteSheetId { get; set; } = "";

    public string PublisherUserId { get; set; } = "";

    public EmergentRouteSheetSnapshot RouteSheetSnapshot { get; set; } = new();

    public DateTimeOffset PublishedAtUtc { get; set; }

    /// <summary>Null = señal activa para recomendaciones.</summary>
    public DateTimeOffset? RetractedAtUtc { get; set; }

    /// <summary>Comentarios públicos de esta publicación (<c>emo_*</c>), independientes del producto/servicio base.</summary>
    public List<OfferQaComment> OfferQa { get; set; } = new();
}
