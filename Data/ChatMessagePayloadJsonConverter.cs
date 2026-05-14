using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Data;

/// <summary>
/// Evita <see cref="JsonPolymorphicAttribute"/> en la raíz (fallos con filas legacy o metadatos raros de STJ);
/// deserializa por <c>type</c> a tipos concretos con opciones simples.
/// El lector requiere un único anclaje; el cuerpo resultante se convierte a tipos C# sin exponer <c>JsonElement</c> hacia arriba.
/// Los discriminadores <c>certificate</c>, <c>agreement</c>, <c>system_text</c>, <c>payment_fee_receipt</c> (JSON histórico)
/// se levantan a <see cref="ChatUnifiedMessagePayload"/> en memoria.
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
                return EmptyFallback();

            var disc = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? "").Trim()
                : "";

            var json = root.GetRawText();
            var o = ChatMessageJson.ConcreteDeserializeOptions;

            return disc switch
            {
                "unified" => DeserializeOrEmpty<ChatUnifiedMessagePayload>(json, o),
                "text" => DeserializeOrEmpty<ChatTextPayload>(json, o),
                "image" => DeserializeOrEmpty<ChatImagePayload>(json, o),
                "audio" => DeserializeOrEmpty<ChatAudioPayload>(json, o),
                "doc" => DeserializeOrEmpty<ChatDocPayload>(json, o),
                "docs" => DeserializeOrEmpty<ChatDocsBundlePayload>(json, o),
                "certificate" => MigrateLegacyCertificate(root, json, o),
                "agreement" => MigrateLegacyAgreement(root, json, o),
                "system_text" => MigrateLegacySystemText(root, json, o),
                "payment_fee_receipt" => MigrateLegacyPaymentFeeReceipt(root, json, o),
                _ => EmptyFallback(),
            };
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

        JsonSerializer.Serialize(writer, value, value.GetType(), ChatMessageJson.ConcreteDeserializeOptions);
    }

    private static ChatMessagePayload DeserializeOrEmpty<T>(string json, JsonSerializerOptions o)
        where T : ChatMessagePayload
    {
        try
        {
            var x = JsonSerializer.Deserialize<T>(json, o);
            return x is null ? EmptyFallback() : x;
        }
        catch
        {
            return EmptyFallback();
        }
    }

    private static IReadOnlyList<string>? ReadReplyToMessageIds(JsonElement root)
    {
        if (!root.TryGetProperty("replyToMessageIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var s = (el.GetString() ?? "").Trim();
            if (s.Length > 0)
                list.Add(s);
        }
        return list.Count > 0 ? list : null;
    }

    private static ChatMessagePayload MigrateLegacyCertificate(JsonElement root, string json, JsonSerializerOptions o)
    {
        try
        {
            var tmp = JsonSerializer.Deserialize<LegacyCertificateJson>(json, o);
            if (tmp is null)
                return EmptyFallback();
            var title = (tmp.Title ?? "").Trim();
            var body = (tmp.Body ?? "").Trim();
            if (title.Length == 0 && body.Length == 0)
                return EmptyFallback();
            return new ChatUnifiedMessagePayload
            {
                Certificate = new ChatUnifiedPlatformCertificateBlock
                {
                    Title = title.Length > 0 ? title : "Certificado",
                    Body = body,
                },
                IssuedByVibeTradePlatform = true,
                ReplyToMessageIds = ReadReplyToMessageIds(root),
            };
        }
        catch
        {
            return EmptyFallback();
        }
    }

    private static ChatMessagePayload MigrateLegacyAgreement(JsonElement root, string json, JsonSerializerOptions o)
    {
        try
        {
            var tmp = JsonSerializer.Deserialize<LegacyAgreementJson>(json, o);
            if (tmp is null)
                return EmptyFallback();
            var aid = (tmp.AgreementId ?? "").Trim();
            if (aid.Length < 1)
                return EmptyFallback();
            var st = (tmp.Status ?? "").Trim();
            if (st is not ("pending_buyer" or "accepted" or "rejected"))
                st = "pending_buyer";
            return new ChatUnifiedMessagePayload
            {
                Agreement = new ChatUnifiedPlatformAgreementBlock
                {
                    AgreementId = aid,
                    Title = (tmp.Title ?? "").Trim(),
                    Body = (tmp.Body ?? "").Trim(),
                    Status = st,
                },
                IssuedByVibeTradePlatform = false,
                ReplyToMessageIds = ReadReplyToMessageIds(root),
            };
        }
        catch
        {
            return EmptyFallback();
        }
    }

    private static ChatMessagePayload MigrateLegacySystemText(JsonElement root, string json, JsonSerializerOptions o)
    {
        try
        {
            var tmp = JsonSerializer.Deserialize<LegacySystemTextJson>(json, o);
            if (tmp is null || string.IsNullOrWhiteSpace(tmp.Text))
                return EmptyFallback();
            return new ChatUnifiedMessagePayload
            {
                SystemText = tmp.Text.Trim(),
                IssuedByVibeTradePlatform = true,
                ReplyToMessageIds = ReadReplyToMessageIds(root),
            };
        }
        catch
        {
            return EmptyFallback();
        }
    }

    private static ChatMessagePayload MigrateLegacyPaymentFeeReceipt(JsonElement root, string json, JsonSerializerOptions o)
    {
        try
        {
            var data = JsonSerializer.Deserialize<ChatPaymentFeeReceiptData>(json, o);
            if (data?.Lines is null)
                return EmptyFallback();
            return new ChatUnifiedMessagePayload
            {
                PaymentFeeReceipt = ChatUnifiedPlatformReceiptMapper.FromPayload(data),
                IssuedByVibeTradePlatform = true,
                ReplyToMessageIds = ReadReplyToMessageIds(root),
            };
        }
        catch
        {
            return EmptyFallback();
        }
    }

    private static ChatUnifiedMessagePayload EmptyFallback() => new() { Text = "" };

    private sealed class LegacyCertificateJson
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
    }

    private sealed class LegacyAgreementJson
    {
        public string? AgreementId { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Status { get; set; }
    }

    private sealed class LegacySystemTextJson
    {
        public string? Text { get; set; }
    }
}
