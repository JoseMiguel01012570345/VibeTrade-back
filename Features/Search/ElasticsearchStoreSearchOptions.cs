namespace VibeTrade.Backend.Features.Search;

/// <summary>Opciones de índice y cliente para búsqueda de tiendas en Elasticsearch.</summary>
public sealed class ElasticsearchStoreSearchOptions
{
    public const string SectionName = "Elasticsearch";

    /// <summary>Si es false, la búsqueda sigue usando EF en memoria y el writer no hace nada.</summary>
    public bool Enabled { get; set; }

    /// <summary>URI del nodo (p. ej. http://localhost:9200).</summary>
    public string? Uri { get; set; }

    /// <summary>Nombre del índice (por defecto vt-stores).</summary>
    public string IndexName { get; set; } = "vt-stores";

    /// <summary>Si true, al arrancar se reindexan todas las tiendas (útil en dev; en prod usar jobs o API).</summary>
    public bool ReindexOnStartup { get; set; }
}
