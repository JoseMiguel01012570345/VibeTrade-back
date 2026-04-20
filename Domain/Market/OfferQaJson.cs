using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VibeTrade.Backend.Domain.Market;

/// <summary>Serialización y conversión EF para el array QA en jsonb.</summary>
public static class OfferQaJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string ToJsonb(IReadOnlyList<OfferQaComment> items) =>
        JsonSerializer.Serialize(items ?? Array.Empty<OfferQaComment>(), SerializerOptions);

    public static List<OfferQaComment> FromJsonb(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<OfferQaComment>();
        try
        {
            var list = JsonSerializer.Deserialize<List<OfferQaComment>>(json, SerializerOptions);
            return list ?? new List<OfferQaComment>();
        }
        catch
        {
            return new List<OfferQaComment>();
        }
    }

    public static JsonNode ToJsonNode(IReadOnlyList<OfferQaComment> items)
    {
        var json = JsonSerializer.Serialize(items ?? Array.Empty<OfferQaComment>(), SerializerOptions);
        return JsonNode.Parse(json) ?? new JsonArray();
    }

    public static ValueConverter<List<OfferQaComment>, string> CreateEfConverter() =>
        new(
            v => ToJsonb(v),
            v => FromJsonb(v));
}
