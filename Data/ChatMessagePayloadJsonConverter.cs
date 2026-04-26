using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Data;

/// <summary>
/// Evita <see cref="JsonPolymorphicAttribute"/> en la raíz (fallos con filas legacy o metadatos raros de STJ);
/// deserializa por <c>type</c> a tipos concretos con opciones simples.
/// El lector requiere un único anclaje; el cuerpo resultante se convierte a tipos C# sin exponer <c>JsonElement</c> hacia arriba.
/// </summary>
internal sealed class ChatMessagePayloadJsonConverter : JsonConverter<ChatMessagePayload>
{
    public override ChatMessagePayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return EmptyText();

            var disc = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? "").Trim()
                : "";

            var json = root.GetRawText();
            var o = ChatMessageJson.ConcreteDeserializeOptions;

            return disc switch
            {
                "text" => DeserializeOrEmpty<ChatTextPayload>(json, o),
                "image" => DeserializeOrEmpty<ChatImagePayload>(json, o),
                "audio" => DeserializeOrEmpty<ChatAudioPayload>(json, o),
                "doc" => DeserializeOrEmpty<ChatDocPayload>(json, o),
                "docs" => DeserializeOrEmpty<ChatDocsBundlePayload>(json, o),
                "certificate" => DeserializeOrEmpty<ChatCertificatePayload>(json, o),
                "agreement" => DeserializeOrEmpty<ChatAgreementPayload>(json, o),
                "system_text" => DeserializeOrEmpty<ChatSystemTextPayload>(json, o),
                _ => EmptyText(),
            };
        }
        catch
        {
            return EmptyText();
        }
    }

    public override void Write(Utf8JsonWriter writer, ChatMessagePayload value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), ChatMessageJson.ConcreteDeserializeOptions);
    }

    private static ChatMessagePayload DeserializeOrEmpty<T>(string json, JsonSerializerOptions o)
        where T : ChatMessagePayload
    {
        try
        {
            var x = JsonSerializer.Deserialize<T>(json, o);
            return x is null ? EmptyText() : x;
        }
        catch
        {
            return EmptyText();
        }
    }

    private static ChatTextPayload EmptyText() => new() { Text = "" };
}
