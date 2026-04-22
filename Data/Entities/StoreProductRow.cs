using VibeTrade.Backend.Domain.Market;

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

    /// <summary>Array JSON de códigos de moneda aceptados para el pago.</summary>
    public string MonedasJson { get; set; } = "[]";

    public string? TaxesShippingInstall { get; set; }

    /// <summary>Disponibilidad (texto en el cliente; p. ej. stock o plazo).</summary>
    public string Availability { get; set; } = "";

    public string WarrantyReturn { get; set; } = "";

    public string ContentIncluded { get; set; } = "";

    public string UsageConditions { get; set; } = "";

    /// <summary>Array JSON de URLs.</summary>
    public string PhotoUrlsJson { get; set; } = "[]";

    public bool Published { get; set; }

    /// <summary>Lista de campos personalizados (JSON, alineado a StoreCustomField[]).</summary>
    public string CustomFieldsJson { get; set; } = "[]";

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
