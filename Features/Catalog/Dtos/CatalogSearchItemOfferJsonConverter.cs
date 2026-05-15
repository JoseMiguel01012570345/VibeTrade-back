using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Catalog.Dtos;

/// <summary>Resumen de producto o servicio en un hit de búsqueda (más angosto que <see cref="HomeOfferViewDto"/> en Home).</summary>
public sealed class CatalogSearchSlimProductOffer
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "product";
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Price { get; set; }
    public string? Currency { get; set; }
    [JsonPropertyName("acceptedCurrencies")]
    public IReadOnlyList<string> AcceptedCurrencies { get; set; } = Array.Empty<string>();
    [JsonPropertyName("photoUrls")]
    public IReadOnlyList<string> PhotoUrls { get; set; } = Array.Empty<string>();
    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; set; }
}

public sealed class CatalogSearchSlimServiceOffer
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "service";
    public string? Category { get; set; }
    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }
    [JsonPropertyName("acceptedCurrencies")]
    public IReadOnlyList<string> AcceptedCurrencies { get; set; } = Array.Empty<string>();
    [JsonPropertyName("photoUrls")]
    public IReadOnlyList<string> PhotoUrls { get; set; } = Array.Empty<string>();
    public string? Descripcion { get; set; }
}

/// <summary>
/// Contenedor: en JSON se serializa solo <b>una</b> de las formas (producto, servicio o emergente), sin envoltura,
/// alineado al contrato previo bajo <c>offer</c>.
/// </summary>
[JsonConverter(typeof(CatalogSearchItemOfferJsonConverter))]
public sealed class CatalogSearchItemOffer
{
    public CatalogSearchSlimProductOffer? Product { get; init; }
    public CatalogSearchSlimServiceOffer? Service { get; init; }
    public HomeOfferViewDto? Emergent { get; init; }
}

public sealed class CatalogSearchItemOfferJsonConverter : JsonConverter<CatalogSearchItemOffer>
{
    public override CatalogSearchItemOffer? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, CatalogSearchItemOffer value, JsonSerializerOptions options)
    {
        if (value.Product is not null)
        {
            JsonSerializer.Serialize(writer, value.Product, options);
            return;
        }

        if (value.Service is not null)
        {
            JsonSerializer.Serialize(writer, value.Service, options);
            return;
        }

        if (value.Emergent is not null)
        {
            JsonSerializer.Serialize(writer, value.Emergent, options);
            return;
        }

        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}