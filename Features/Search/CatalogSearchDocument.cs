using Elastic.Clients.Elasticsearch;

namespace VibeTrade.Backend.Features.Search;

/// <summary>Documento unificado en Elasticsearch: tienda, producto o servicio.</summary>
public sealed class CatalogSearchDocument
{
    /// <summary>Nombre JSON del campo <see cref="VtCatalogSk"/>; debe mapearse siempre como <c>keyword</c> (sort / wildcard).</summary>
    public const string ElasticsearchVtCatalogSkField = "vtCatalogSk";

    /// <summary>Nombre JSON del campo geo para distancia; debe mapearse como <c>geo_point</c>.</summary>
    public const string ElasticsearchVtLocationField = "vtLocation";

    /// <summary><c>store</c>, <c>product</c> o <c>service</c>.</summary>
    public string Kind { get; set; } = "";

    public string StoreId { get; set; } = "";

    /// <summary>Id de oferta (producto/servicio); vacío para <see cref="Kind"/> tienda.</summary>
    public string OfferId { get; set; } = "";

    /// <summary>Título principal para ordenar y mostrar (nombre tienda / producto / servicio).</summary>
    public string Name { get; set; } = "";

    /// <summary>Clave en minúsculas solo para ordenar / wildcard; índice en ES como <see cref="ElasticsearchVtCatalogSkField"/> (<c>keyword</c> vía PutMapping explícito).</summary>
    public string VtCatalogSk { get; set; } = "";

    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    /// <summary>Texto agregado para match léxico y coherente con el embedding TF‑IDF.</summary>
    public string SearchText { get; set; } = "";

    public LatLonGeoLocation? Location { get; set; }

    /// <summary>Campo geo estable (geo_point) para sort y filtros de distancia, evitando conflictos con mappings viejos de <see cref="Location"/>.</summary>
    public LatLonGeoLocation? VtLocation { get; set; }

    public int TrustScore { get; set; }

    public long PublishedProducts { get; set; }

    public long PublishedServices { get; set; }

    public float[]? NameSemanticVector { get; set; }
}
