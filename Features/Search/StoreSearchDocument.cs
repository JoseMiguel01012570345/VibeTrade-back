using Elastic.Clients.Elasticsearch;

namespace VibeTrade.Backend.Features.Search;

/// <summary>Documento indexado para búsqueda de tiendas.</summary>
public sealed class StoreSearchDocument
{
    public string StoreId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Copia en <c>keyword</c> para ordenar (el campo <c>name</c> es <c>text</c>).</summary>
    public string NameSort { get; set; } = "";

    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    /// <summary>WGS84; null si la tienda no tiene pin.</summary>
    public LatLonGeoLocation? Location { get; set; }

    public int TrustScore { get; set; }

    public int PublishedProducts { get; set; }

    public int PublishedServices { get; set; }
}
