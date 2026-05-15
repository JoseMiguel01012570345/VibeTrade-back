using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VibeTrade.Backend.Features.Catalog;

/// <summary><see cref="ArgumentException.ParamName"/> para mapear errores de catálogo en la API.</summary>
public static class CatalogArgumentParams
{
    public const string Currency = "catalogCurrency";
    public const string Validation = "catalogValidation";
}

/// <summary>Categorías permitidas al crear fichas de producto y servicio (alineado al cliente flow-ui).</summary>
public static class CatalogCategories
{
    public static readonly IReadOnlyList<string> ProductAndService = new[]
    {
        "Cosechas",
        "Insumos",
        "Mercancías",
        "Alimentos",
        "B2B",
        "Servicios",
        "Asesoría",
        "Logística",
        "Transportista",
    };
}

/// <summary>Códigos de moneda para fichas, hojas de ruta y selectores (una sola lista en servidor).</summary>
public static class CatalogCurrencies
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "CUP",
        "USD",
        "EUR",
        "CAD",
        "GBP",
    };
}

/// <summary>Opciones compartidas para cuerpos de catálogo / workspace (camelCase, Web).</summary>
public static class MarketJsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

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

    public static ValueConverter<List<OfferQaComment>, string> CreateEfConverter() =>
        new(
            v => ToJsonb(v),
            v => FromJsonb(v));
}

/// <summary>Parse de JSON en el límite hacia estructuras (p. ej. <c>demo-seed</c> o columnas leídas como texto en migraciones heredadas).</summary>
internal static class CatalogJsonColumnParsing
{
    public static IReadOnlyList<string> StringListOrEmpty(IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
            return Array.Empty<string>();
        return values
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public static IReadOnlyList<string> StringListOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, MarketJsonDefaults.Options) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<StoreCustomFieldBody> CustomFieldsListOrEmpty(IReadOnlyList<StoreCustomFieldBody>? values)
    {
        if (values is not { Count: > 0 })
            return Array.Empty<StoreCustomFieldBody>();
        return values;
    }

    public static IReadOnlyList<StoreCustomFieldBody> CustomFieldsListOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<StoreCustomFieldBody>();
        try
        {
            return JsonSerializer.Deserialize<List<StoreCustomFieldBody>>(json, MarketJsonDefaults.Options)
                ?? new List<StoreCustomFieldBody>();
        }
        catch
        {
            return Array.Empty<StoreCustomFieldBody>();
        }
    }
}
