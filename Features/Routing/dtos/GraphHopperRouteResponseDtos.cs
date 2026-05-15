using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Routing.Dtos;

/// <summary>Respuesta mínima de <c>GET /route</c> (GraphHopper JSON).</summary>
internal sealed class GhRouteEnvelope
{
    [JsonPropertyName("paths")]
    public List<GhPathDto>? Paths { get; set; }
}

internal sealed class GhPathDto
{
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("points")]
    public GhGeoJsonDto? Points { get; set; }
}

internal sealed class GhGeoJsonDto
{
    [JsonPropertyName("coordinates")]
    public List<List<double>>? Coordinates { get; set; }
}
