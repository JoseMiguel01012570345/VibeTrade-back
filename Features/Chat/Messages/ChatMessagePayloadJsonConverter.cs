using System.Text.Json;
using System.Text.Json.Serialization;
using VibeTrade.Backend.Features.Chat.Dtos;

namespace VibeTrade.Backend.Features.Chat.Messages;

/// <summary>
/// Serializa y deserializa <see cref="ChatUnifiedMessagePayload"/> en <c>PayloadJson</c> (jsonb).
/// Sin discriminador <c>type</c> en JSON: el cuerpo es el propio objeto unificado.
/// </summary>
internal sealed class ChatMessagePayloadJsonConverter : JsonConverter<ChatMessagePayload>
{
    private static readonly JsonSerializerOptions SerializeOptions = ChatMessageJson.ConcreteDeserializeOptions;

    public override ChatMessagePayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return EmptyFallback();

            var json = doc.RootElement.GetRawText();
            var x = JsonSerializer.Deserialize<ChatUnifiedMessagePayload>(json, SerializeOptions);
            return x ?? EmptyFallback();
        }
        catch
        {
            return EmptyFallback();
        }
    }

    public override void Write(Utf8JsonWriter writer, ChatMessagePayload value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var unified = value as ChatUnifiedMessagePayload ?? EmptyFallback();
        JsonSerializer.Serialize(writer, unified, typeof(ChatUnifiedMessagePayload), SerializeOptions);
    }

    private static ChatUnifiedMessagePayload EmptyFallback() => new() { Text = "" };
}
