namespace VibeTrade.Backend.Features.Market.Entities;

/// <summary>Producto de catálogo de una tienda.</summary>
public sealed class StoreProductRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public string Category { get; set; } = "";

    /// <summary>Ids de categorías jerárquicas (jsonb: <c>CategoryIdsJson</c>).</summary>
    public List<string> CategoryIds { get; set; } = new();

    public string? SupplierId { get; set; }

    public StoreSupplierRow? Supplier { get; set; }

    public bool PendingApproval { get; set; }

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

    /// <summary>Existencias controladas; <c>null</c> = sin control de stock (ilimitado para checkout).</summary>
    public int? StockQuantity { get; set; }

    /// <summary>Unidades vendidas acumuladas (se incrementa en cada pedido).</summary>
    public int UnitsSold { get; set; }

    public string WarrantyReturn { get; set; } = "";

    public string ContentIncluded { get; set; } = "";

    public string UsageConditions { get; set; } = "";

    /// <summary>URLs (jsonb: <c>PhotoUrlsJson</c>).</summary>
    public List<string> PhotoUrls { get; set; } = new();

    public bool Published { get; set; }

    /// <summary>Campos personalizados (jsonb: <c>CustomFieldsJson</c>).</summary>
    public List<StoreCustomFieldBody> CustomFields { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Borrado lógico; null = activo en catálogo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>
    /// Peso de popularidad (últimos 30 días): interacciones ponderadas + likes a oferta + likes a comentarios × 0,25.
    /// Denormalizado para lectura rápida en recomendaciones.
    /// </summary>
    public double PopularityWeight { get; set; }
}
