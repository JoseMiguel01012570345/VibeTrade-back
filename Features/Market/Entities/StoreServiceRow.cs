namespace VibeTrade.Backend.Features.Market.Entities;

/// <summary>Servicio de catálogo de una tienda.</summary>
public sealed class StoreServiceRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public bool? Published { get; set; }

    public string Category { get; set; } = "";

    public string NombreServicio { get; set; } = "";

    public string Descripcion { get; set; } = "";

    public ServiceRiesgosBody Riesgos { get; set; } = new();

    public string Incluye { get; set; } = "";

    public string NoIncluye { get; set; } = "";

    public ServiceDependenciasBody Dependencias { get; set; } = new();

    public string Entregables { get; set; } = "";

    public ServiceGarantiasBody Garantias { get; set; } = new();

    public string PropIntelectual { get; set; } = "";

    /// <summary>Precio fijo del servicio en checkout (moneda <see cref="CurrencyCode"/>).</summary>
    public decimal FixedPrice { get; set; }

    /// <summary>Moneda del precio fijo; debe ser USD.</summary>
    public string CurrencyCode { get; set; } = "USD";

    /// <summary>Mes (1–12) de la única recurrencia de pago del contrato de catálogo.</summary>
    public int RecurrenceMonth { get; set; } = 1;

    /// <summary>Día (1–31) de la única recurrencia de pago del contrato de catálogo.</summary>
    public int RecurrenceDay { get; set; } = 1;

    public List<StoreCustomFieldBody> CustomFields { get; set; } = new();

    public List<string> PhotoUrls { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Borrado lógico; null = activo en catálogo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>
    /// Peso de popularidad (últimos 30 días): interacciones ponderadas + likes a oferta + likes a comentarios × 0,25.
    /// </summary>
    public double PopularityWeight { get; set; }
}
