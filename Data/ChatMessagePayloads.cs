using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Data;

/// <summary>
/// Contenido persistido del mensaje de chat (columna <c>PayloadJson</c>, jsonb).
/// Serialización con <see cref="ChatMessageJson.Options"/> (camelCase Web).
/// </summary>
[JsonConverter(typeof(ChatMessagePayloadJsonConverter))]
public abstract record ChatMessagePayload
{
    /// <summary>
    /// Discriminador; coincide con la propiedad JSON <c>type</c>.
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Ids denormalizados de mensajes citados (mismo hilo). En mensajes <see cref="ChatUnifiedMessagePayload"/> se rellenan desde <see cref="ChatUnifiedMessagePayload.RepliesTo"/>.
    /// </summary>
    public IReadOnlyList<string>? ReplyToMessageIds { get; init; }
}

public sealed record ReplyQuoteDto
{
    public required string MessageId { get; init; }

    /// <summary>Etiqueta mostrada junto a la cita (nombre tienda / comprador).</summary>
    public required string Author { get; init; }

    /// <summary>Vista previa en texto del mensaje citado.</summary>
    public required string Preview { get; init; }

    /// <summary>UTC timestamp of the original message.</summary>
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
    public required string Size { get; init; } // e.g. "1.2 MB"
    public required string Kind { get; init; } // "pdf" | "doc" | "other"
    public string? Url { get; init; }
}

/// <summary>Certificado dentro de <see cref="ChatUnifiedMessagePayload"/> (VibeTrade).</summary>
public sealed record ChatUnifiedPlatformCertificateBlock
{
    public required string Title { get; init; }
    public required string Body { get; init; }
}

/// <summary>Acuerdo dentro de <see cref="ChatUnifiedMessagePayload"/> (VibeTrade).</summary>
public sealed record ChatUnifiedPlatformAgreementBlock
{
    public required string AgreementId { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public required string Status { get; init; }
}

public sealed record ChatTextPayload : ChatMessagePayload
{
    public ChatTextPayload() => Type = "text";

    public required string Text { get; init; }

    /// <summary>Optional id of an associated offer Q&amp;A entry.</summary>
    public string? OfferQaId { get; init; }

    public IReadOnlyList<ReplyQuoteDto>? ReplyQuotes { get; init; }
}

public sealed record ChatImagePayload : ChatMessagePayload
{
    public ChatImagePayload() => Type = "image";

    public required IReadOnlyList<ChatImageDto> Images { get; init; }

    public string? Caption { get; init; }

    /// <summary>Optional short audio comment embedded in the image message.</summary>
    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }

    public IReadOnlyList<ReplyQuoteDto>? ReplyQuotes { get; init; }
}

public sealed record ChatAudioPayload : ChatMessagePayload
{
    public ChatAudioPayload() => Type = "audio";

    public required string Url { get; init; }

    public required int Seconds { get; init; }

    public IReadOnlyList<ReplyQuoteDto>? ReplyQuotes { get; init; }
}

public sealed record ChatDocPayload : ChatMessagePayload
{
    public ChatDocPayload() => Type = "doc";

    public required string Name { get; init; }
    public required string Size { get; init; }
    public required string Kind { get; init; } // "pdf" | "doc" | "other"
    public string? Url { get; init; }

    public string? Caption { get; init; }

    public IReadOnlyList<ReplyQuoteDto>? ReplyQuotes { get; init; }
}

public sealed record ChatDocsBundlePayload : ChatMessagePayload
{
    public ChatDocsBundlePayload() => Type = "docs";

    public required IReadOnlyList<ChatDocumentDto> Documents { get; init; }

    public string? Caption { get; init; }

    /// <summary>Optional short audio comment embedded in the bundle message.</summary>
    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }

    public IReadOnlyList<ReplyQuoteDto>? ReplyQuotes { get; init; }
}

/// <summary>
/// Mensaje de usuario: un solo JSON con medios opcionales y <see cref="RepliesTo"/> (mensajes del hilo a los que responde; usar <see cref="ReplyQuoteDto.MessageId"/>, no timestamps, como ancla).
/// </summary>
public sealed record ChatUnifiedMessagePayload : ChatMessagePayload
{
    public ChatUnifiedMessagePayload() => Type = "unified";

    public string? Text { get; init; }

    public string? OfferQaId { get; init; }

    public IReadOnlyList<ChatImageDto>? Images { get; init; }

    public IReadOnlyList<ChatDocumentDto>? Documents { get; init; }

    public string? Caption { get; init; }

    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }

    /// <summary>Nota de voz (opcional; independiente de adjuntos).</summary>
    public string? VoiceUrl { get; init; }

    public int? VoiceSeconds { get; init; }

    /// <summary>Mensajes del hilo a los que responde (id + vista previa + autor + instante del mensaje citado).</summary>
    public IReadOnlyList<ReplyQuoteDto>? RepliesTo { get; init; }

    /// <summary>Texto de sistema (no es el mensaje de chat del usuario; ver <see cref="Text"/>).</summary>
    public string? SystemText { get; init; }

    /// <summary>Certificado emitido en nombre de VibeTrade.</summary>
    public ChatUnifiedPlatformCertificateBlock? Certificate { get; init; }

    /// <summary>Acuerdo anunciado en el hilo (VibeTrade / flujo contractual).</summary>
    public ChatUnifiedPlatformAgreementBlock? Agreement { get; init; }

    /// <summary>Recibo de tarifa post-pago (VibeTrade).</summary>
    public ChatUnifiedPlatformPaymentFeeReceiptBlock? PaymentFeeReceipt { get; init; }

    /// <summary>True cuando el bloque platform lo emite VibeTrade (avisos automáticos, recibo, acuerdo contractual, certificado).</summary>
    public bool IssuedByVibeTradePlatform { get; init; }
}

/// <summary>Línea del desglose (mercancía, servicio, tramo, etc.).</summary>
public sealed record ChatPaymentFeeReceiptLineDto
{
    public required string Label { get; init; }

    public required long AmountMinor { get; init; }
}

/// <summary>Recibo de tarifa dentro de <see cref="ChatUnifiedMessagePayload"/> (VibeTrade).</summary>
public sealed record ChatUnifiedPlatformPaymentFeeReceiptBlock
{
    public required string AgreementId { get; init; }
    public required string AgreementTitle { get; init; }
    public required string PaymentId { get; init; }
    public required string CurrencyLower { get; init; }
    public required long SubtotalMinor { get; init; }
    public required long ClimateMinor { get; init; }
    public required long StripeFeeMinorActual { get; init; }
    public required long StripeFeeMinorEstimated { get; init; }
    public required long TotalChargedMinor { get; init; }
    public required string StripePricingUrl { get; init; }
    public required List<ChatPaymentFeeReceiptLineDto> Lines { get; init; } = [];
    public string InvoiceIssuerPlatform { get; init; } = "VibeTrade";
    public string InvoiceStoreName { get; init; } = "";
}

/// <summary>Mapea bloque recibo unificado ↔ <see cref="ChatPaymentFeeReceiptData"/> (PDF / email / servicios).</summary>
public static class ChatUnifiedPlatformReceiptMapper
{
    public static ChatUnifiedPlatformPaymentFeeReceiptBlock FromPayload(ChatPaymentFeeReceiptData p) =>
        new()
        {
            AgreementId = p.AgreementId,
            AgreementTitle = p.AgreementTitle,
            PaymentId = p.PaymentId,
            CurrencyLower = p.CurrencyLower,
            SubtotalMinor = p.SubtotalMinor,
            ClimateMinor = p.ClimateMinor,
            StripeFeeMinorActual = p.StripeFeeMinorActual,
            StripeFeeMinorEstimated = p.StripeFeeMinorEstimated,
            TotalChargedMinor = p.TotalChargedMinor,
            StripePricingUrl = p.StripePricingUrl,
            Lines = p.Lines,
            InvoiceIssuerPlatform = p.InvoiceIssuerPlatform,
            InvoiceStoreName = p.InvoiceStoreName,
        };

    public static ChatPaymentFeeReceiptData ToData(ChatUnifiedPlatformPaymentFeeReceiptBlock b) =>
        new()
        {
            AgreementId = b.AgreementId,
            AgreementTitle = b.AgreementTitle,
            PaymentId = b.PaymentId,
            CurrencyLower = b.CurrencyLower,
            SubtotalMinor = b.SubtotalMinor,
            ClimateMinor = b.ClimateMinor,
            StripeFeeMinorActual = b.StripeFeeMinorActual,
            StripeFeeMinorEstimated = b.StripeFeeMinorEstimated,
            TotalChargedMinor = b.TotalChargedMinor,
            StripePricingUrl = b.StripePricingUrl,
            Lines = b.Lines,
            InvoiceIssuerPlatform = b.InvoiceIssuerPlatform,
            InvoiceStoreName = b.InvoiceStoreName,
        };
}

/// <summary>
/// Datos del recibo post-pago (tarifa Stripe, desglose). No es un <see cref="ChatMessagePayload"/>; en hilo se persiste dentro de <see cref="ChatUnifiedMessagePayload.PaymentFeeReceipt"/>.
/// </summary>
public sealed record ChatPaymentFeeReceiptData
{
    public required string AgreementId { get; init; }

    public required string AgreementTitle { get; init; }

    public required string PaymentId { get; init; }

    public required string CurrencyLower { get; init; }

    public required long SubtotalMinor { get; init; }

    public required long ClimateMinor { get; init; }

    /// <summary>Tarifa registrada por Stripe en esta operación (minor units).</summary>
    public required long StripeFeeMinorActual { get; init; }

    /// <summary>Estimación previa al cobro (2,9 % + fijo), para comparación.</summary>
    public required long StripeFeeMinorEstimated { get; init; }

    public required long TotalChargedMinor { get; init; }

    public required string StripePricingUrl { get; init; }

    public required List<ChatPaymentFeeReceiptLineDto> Lines { get; init; } = [];

    /// <summary>Plataforma emisora del documento (factura / informe).</summary>
    public string InvoiceIssuerPlatform { get; init; } = "VibeTrade";

    /// <summary>Tienda del chat (vendedor) asociada al cobro.</summary>
    public string InvoiceStoreName { get; init; } = "";
}
