using System.Text.Json;

namespace VibeTrade.Backend.Features.Notifications;

public static class NotificationUtils
{
    /// <summary>Trunca previews largos a un máximo de caracteres, agregando «…» si corresponde.</summary>
    public static string TruncatePreview(string text, int maxLength = 500)
    {
        text ??= "";
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    /// <summary>Normaliza etiquetas de autor / sujeto; si está vacía, usa el fallback.</summary>
    public static string NormalizeLabel(string? raw, string fallback)
    {
        var t = (raw ?? "").Trim();
        return t.Length > 0 ? t : fallback;
    }

    /// <summary>Serializa un meta sencillo a JSON usando camelCase por defecto.</summary>
    public static string SerializeMeta(object value, JsonSerializerOptions? options = null)
    {
        options ??= new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Serialize(value, options);
    }
}

