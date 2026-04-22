using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Data.RouteSheets;

/// <summary>Serialización JSON alineada con el cliente (camelCase).</summary>
public static class RouteSheetJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
