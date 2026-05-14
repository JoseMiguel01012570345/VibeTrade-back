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
            return new ChatUnifiedMessagePayload { Text = "" };

        try
        {
            return JsonSerializer.Deserialize<ChatMessagePayload>(json, Options)
                ?? new ChatUnifiedMessagePayload { Text = "" };
        }
        catch
        {
            return new ChatUnifiedMessagePayload { Text = "" };
        }
    }
}
