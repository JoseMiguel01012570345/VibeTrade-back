using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>
/// Raíz del payload persistido en <c>PayloadJson</c> (jsonb). Solo existe la forma concreta
/// <see cref="ChatUnifiedMessagePayload"/>; no se persiste discriminador <c>type</c> en JSON.
/// </summary>
[JsonConverter(typeof(ChatMessagePayloadJsonConverter))]
public abstract record ChatMessagePayload
{
    [JsonIgnore]
    public string Type { get; init; } = "";

    public IReadOnlyList<string>? ReplyToMessageIds { get; init; }
}

public sealed record ReplyQuoteDto
{
    public required string MessageId { get; init; }
    public required string Author { get; init; }
    public required string Preview { get; init; }
    public required DateTimeOffset AtUtc { get; init; }
}

public sealed record ChatEmbeddedAudioDto
{
    public required string Url { get; init; }
    public required int Seconds { get; init; }
}

public sealed record ChatImageDto
{
    public required string Url { get; init; }
}

public sealed record ChatDocumentDto
{
    public required string Name { get; init; }
    public required string Size { get; init; }
    public required string Kind { get; init; }
    public string? Url { get; init; }
}

public sealed record ChatUnifiedPlatformCertificateBlock
{
    public required string Title { get; init; }
    public required string Body { get; init; }
}

public sealed record ChatUnifiedPlatformAgreementBlock
{
    public required string AgreementId { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public required string Status { get; init; }
}

public sealed record ChatUnifiedMessagePayload : ChatMessagePayload
{
    public string? Text { get; init; }
    public IReadOnlyList<ChatImageDto>? Images { get; init; }
    public IReadOnlyList<ChatDocumentDto>? Documents { get; init; }
    public string? Caption { get; init; }
    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }
    public string? VoiceUrl { get; init; }
    public int? VoiceSeconds { get; init; }
    public IReadOnlyList<ReplyQuoteDto>? RepliesTo { get; init; }
    public string? SystemText { get; init; }
    public ChatUnifiedPlatformCertificateBlock? Certificate { get; init; }
    public ChatUnifiedPlatformAgreementBlock? Agreement { get; init; }
    public ChatUnifiedPlatformPaymentFeeReceiptBlock? PaymentFeeReceipt { get; init; }
    public bool IssuedByVibeTradePlatform { get; init; }
}

public sealed record ChatPaymentFeeReceiptLineDto
{
    public required string Label { get; init; }
    public required long AmountMinor { get; init; }
}

public sealed record ChatUnifiedPlatformPaymentFeeReceiptBlock
{
    public required string AgreementId { get; init; }
    public required string AgreementTitle { get; init; }
    public required string PaymentId { get; init; }
    public required string CurrencyLower { get; init; }
    public required long SubtotalMinor { get; init; }
    public required long ClimateMinor { get; init; }
    public required long ProcessorFeeMinorActual { get; init; }
    public required long ProcessorFeeMinorEstimated { get; init; }
    public required long TotalChargedMinor { get; init; }
    public required string PaymentFeePolicyUrl { get; init; }
    public required List<ChatPaymentFeeReceiptLineDto> Lines { get; init; } = [];
    public string InvoiceIssuerPlatform { get; init; } = "VibeTrade";
    public string InvoiceStoreName { get; init; } = "";
}

public sealed record ChatPaymentFeeReceiptData
{
    public required string AgreementId { get; init; }
    public required string AgreementTitle { get; init; }
    public required string PaymentId { get; init; }
    public required string CurrencyLower { get; init; }
    public required long SubtotalMinor { get; init; }
    public required long ClimateMinor { get; init; }
    public required long ProcessorFeeMinorActual { get; init; }
    public required long ProcessorFeeMinorEstimated { get; init; }
    public required long TotalChargedMinor { get; init; }
    public required string PaymentFeePolicyUrl { get; init; }
    public required List<ChatPaymentFeeReceiptLineDto> Lines { get; init; } = [];
    public string InvoiceIssuerPlatform { get; init; } = "VibeTrade";
    public string InvoiceStoreName { get; init; } = "";
}

/// <summary>Serializa y deserializa <see cref="ChatUnifiedMessagePayload"/> en jsonb.</summary>
public sealed class ChatMessagePayloadJsonConverter : JsonConverter<ChatMessagePayload>
{
    private static readonly JsonSerializerOptions SerializeOptions = new(JsonSerializerDefaults.Web);

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
        JsonSerializer.Serialize(writer, unified, SerializeOptions);
    }

    private static ChatUnifiedMessagePayload EmptyFallback() => new() { Text = "" };
}
