using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>Servicio de catálogo de una tienda.</summary>
public sealed class StoreServiceRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public bool? Published { get; set; }

    public string Category { get; set; } = "";

    public string TipoServicio { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public ServiceRiesgosBody Riesgos { get; set; } = new();

    public string Incluye { get; set; } = "";

    public string NoIncluye { get; set; } = "";

    public ServiceDependenciasBody Dependencias { get; set; } = new();

    public string Entregables { get; set; } = "";

    public ServiceGarantiasBody Garantias { get; set; } = new();

    public string PropIntelectual { get; set; } = "";

    public List<string> Monedas { get; set; } = new();

    public List<StoreCustomFieldBody> CustomFields { get; set; } = new();

    public List<string> PhotoUrls { get; set; } = new();

    /// <summary>Preguntas y respuestas públicas (jsonb <c>OfferQaJson</c>).</summary>
    public List<OfferQaComment> OfferQa { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Borrado lógico; null = activo en catálogo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>
    /// Peso de popularidad (últimos 30 días): interacciones ponderadas + likes a oferta + likes a comentarios × 0,25.
    /// </summary>
    public double PopularityWeight { get; set; }
}
