using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Features.Market;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>Producto de catálogo de una tienda.</summary>
public sealed class StoreProductRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public string Category { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Model { get; set; }

    public string ShortDescription { get; set; } = "";

    public string MainBenefit { get; set; } = "";

    public string TechnicalSpecs { get; set; } = "";

    public string Condition { get; set; } = "";

    public string Price { get; set; } = "";

    /// <summary>Moneda en la que está expresado el precio (código ISO; una sola).</summary>
    public string? MonedaPrecio { get; set; }

    /// <summary>Códigos de moneda aceptados (jsonb: <c>MonedasJson</c>).</summary>
    public List<string> Monedas { get; set; } = new();

    public string? TaxesShippingInstall { get; set; }

    /// <summary>Si el transporte está incluido en este producto (según ficha).</summary>
    public bool TransportIncluded { get; set; }

    /// <summary>Disponibilidad (texto en el cliente; p. ej. stock o plazo).</summary>
    public string Availability { get; set; } = "";

    public string WarrantyReturn { get; set; } = "";

    public string ContentIncluded { get; set; } = "";

    public string UsageConditions { get; set; } = "";

    /// <summary>URLs (jsonb: <c>PhotoUrlsJson</c>).</summary>
    public List<string> PhotoUrls { get; set; } = new();

    public bool Published { get; set; }

    /// <summary>Campos personalizados (jsonb: <c>CustomFieldsJson</c>).</summary>
    public List<StoreCustomFieldBody> CustomFields { get; set; } = new();

    /// <summary>Preguntas y respuestas públicas (jsonb <c>OfferQaJson</c>).</summary>
    public List<OfferQaComment> OfferQa { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Borrado lógico; null = activo en catálogo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>
    /// Peso de popularidad (últimos 30 días): interacciones ponderadas + likes a oferta + likes a comentarios × 0,25.
    /// Denormalizado para lectura rápida en recomendaciones.
    /// </summary>
    public double PopularityWeight { get; set; }
}
