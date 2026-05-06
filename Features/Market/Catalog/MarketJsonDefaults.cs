using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Market.Catalog;

/// <summary>Opciones compartidas para cuerpos de catálogo / workspace (camelCase, Web).</summary>
public static class MarketJsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
