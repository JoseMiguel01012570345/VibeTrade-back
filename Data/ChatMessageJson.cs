using System.Text.Json;

namespace VibeTrade.Backend.Data;

/// <summary>Serialización compartida para <see cref="ChatMessagePayload"/> (DB, API).</summary>
public static class ChatMessageJson
{
    /// <summary>Web defaults sin converter recursivo: usado al serializar tipos concretos de payload.</summary>
    internal static readonly JsonSerializerOptions ConcreteDeserializeOptions = new(JsonSerializerDefaults.Web);

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static ChatMessagePayload DeserializePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ChatTextPayload { Text = "" };

        try
        {
            return JsonSerializer.Deserialize<ChatMessagePayload>(json, Options)
                ?? new ChatTextPayload { Text = "" };
        }
        catch
        {
            return new ChatTextPayload { Text = "" };
        }
    }
}
