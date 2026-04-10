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

    /// <summary>Objeto { enabled, items } en JSON.</summary>
    public string RiesgosJson { get; set; } = "{\"enabled\":false,\"items\":[]}";

    public string Incluye { get; set; } = "";

    public string NoIncluye { get; set; } = "";

    /// <summary>Objeto { enabled, items } en JSON.</summary>
    public string DependenciasJson { get; set; } = "{\"enabled\":false,\"items\":[]}";

    public string Entregables { get; set; } = "";

    /// <summary>Objeto { enabled, texto } en JSON.</summary>
    public string GarantiasJson { get; set; } = "{\"enabled\":false,\"texto\":\"\"}";

    public string PropIntelectual { get; set; } = "";

    /// <summary>Array JSON de códigos de moneda aceptados para el pago (mismo contrato que productos).</summary>
    public string MonedasJson { get; set; } = "[]";

    /// <summary>Lista StoreCustomField[] en JSON.</summary>
    public string CustomFieldsJson { get; set; } = "[]";

    /// <summary>Array JSON de URLs de imágenes (mismo contrato que productos).</summary>
    public string PhotoUrlsJson { get; set; } = "[]";

    /// <summary>Preguntas y respuestas públicas de la oferta (JSON array).</summary>
    public string OfferQaJson { get; set; } = "[]";

    public DateTimeOffset UpdatedAt { get; set; }
}
