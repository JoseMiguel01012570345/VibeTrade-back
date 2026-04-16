namespace VibeTrade.Backend.Features.Search;

/// <summary>Opciones de índice y cliente para búsqueda de tiendas en Elasticsearch.</summary>
public sealed class ElasticsearchStoreSearchOptions
{
    public const string SectionName = "Elasticsearch";

    /// <summary>Si es false, la búsqueda sigue usando EF en memoria y el writer no hace nada.</summary>
    public bool Enabled { get; set; }

    /// <summary>URI del nodo (p. ej. http://localhost:9200).</summary>
    public string? Uri { get; set; }

    /// <summary>Nombre del índice (tiendas + productos + servicios; por defecto vt-catalog).</summary>
    public string IndexName { get; set; } = "vt-catalog";

    /// <summary>Si true, al arrancar se reindexan todas las tiendas (útil en dev; en prod usar jobs o API).</summary>
    public bool ReindexOnStartup { get; set; }

    /// <summary>
    /// Dimensión del vector TF‑IDF (ML.NET): máximo de unigramas (p. ej. 256 o 512).
    /// Si es 0, no se indexa ni consulta kNN (solo búsqueda léxica).
    /// </summary>
    public int SemanticVectorDimensions { get; set; }
}
