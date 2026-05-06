using System.Text.Json.Serialization;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Shape alineado a <c>Offer</c> en Home / feed / búsqueda (sin JSON opaco).</summary>
public sealed class HomeOfferViewDto
{
    public string Id { get; set; } = "";
    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = "";
    public string? Title { get; set; }
    public string? Price { get; set; }
    public string? Currency { get; set; }
    [JsonPropertyName("acceptedCurrencies")]
    public IReadOnlyList<string> AcceptedCurrencies { get; set; } = Array.Empty<string>();
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
    [JsonPropertyName("imageUrls")]
    public IReadOnlyList<string> ImageUrls { get; set; } = Array.Empty<string>();
    /// <summary>Null si el JSON no incluye <c>qa</c> (p. ej. parche solo con otras propiedades).</summary>
    public List<OfferQaComment>? Qa { get; set; }

    public string? Category { get; set; }
    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }
    public string? Incluye { get; set; }
    [JsonPropertyName("noIncluye")]
    public string? NoIncluye { get; set; }
    public string? Entregables { get; set; }
    [JsonPropertyName("propIntelectual")]
    public string? PropIntelectual { get; set; }

    [JsonPropertyName("emergentBaseOfferId")]
    public string? EmergentBaseOfferId { get; set; }
    [JsonPropertyName("emergentThreadId")]
    public string? EmergentThreadId { get; set; }
    [JsonPropertyName("emergentRouteSheetId")]
    public string? EmergentRouteSheetId { get; set; }
    [JsonPropertyName("isEmergentRoutePublication")]
    public bool? IsEmergentRoutePublication { get; set; }
    [JsonPropertyName("emergentMonedaPago")]
    public string? EmergentMonedaPago { get; set; }
    [JsonPropertyName("emergentRouteParadas")]
    public IReadOnlyList<EmergentRouteParadaViewDto> EmergentRouteParadas { get; set; } = Array.Empty<EmergentRouteParadaViewDto>();

    [JsonPropertyName("publicCommentCount")]
    public int? PublicCommentCount { get; set; }
    [JsonPropertyName("offerLikeCount")]
    public int? OfferLikeCount { get; set; }
    [JsonPropertyName("viewerLikedOffer")]
    public bool? ViewerLikedOffer { get; set; }
}

public sealed class EmergentRouteParadaViewDto
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public int Orden { get; set; }
    [JsonPropertyName("stopId")]
    public string? StopId { get; set; }
    [JsonPropertyName("origenLat")]
    public string? OrigenLat { get; set; }
    [JsonPropertyName("origenLng")]
    public string? OrigenLng { get; set; }
    [JsonPropertyName("destinoLat")]
    public string? DestinoLat { get; set; }
    [JsonPropertyName("destinoLng")]
    public string? DestinoLng { get; set; }
    [JsonPropertyName("monedaPago")]
    public string? MonedaPago { get; set; }
    [JsonPropertyName("precioTransportista")]
    public string? PrecioTransportista { get; set; }

    [JsonPropertyName("osrmRoadKm")]
    public double? OsrmRoadKm { get; set; }

    [JsonPropertyName("osrmRouteLatLngs")]
    public List<List<double>>? OsrmRouteLatLngs { get; set; }
}
