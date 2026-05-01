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

public sealed record ChatCertificatePayload : ChatMessagePayload
{
    public ChatCertificatePayload() => Type = "certificate";

    public required string Title { get; init; }
    public required string Body { get; init; }
}

public sealed record ChatAgreementPayload : ChatMessagePayload
{
    public ChatAgreementPayload() => Type = "agreement";

    /// <summary>Id del registro en <c>trade_agreements</c> (cliente: <c>agreementId</c> en el mensaje).</summary>
    public required string AgreementId { get; init; }

    public required string Title { get; init; }

    public string Body { get; init; } = "";

    /// <summary><c>pending_buyer</c> | <c>accepted</c> | <c>rejected</c>.</summary>
    public required string Status { get; init; }
}

/// <summary>
/// System-only informational text, not authored by buyer/seller.
/// </summary>
public sealed record ChatSystemTextPayload : ChatMessagePayload
{
    public ChatSystemTextPayload() => Type = "system_text";

    public required string Text { get; init; }
}

/// <summary>Línea del desglose (mercancía, servicio, tramo, etc.).</summary>
public sealed record ChatPaymentFeeReceiptLineDto
{
    public required string Label { get; init; }

    public required long AmountMinor { get; init; }
}

/// <summary>
/// Recibo post-pago con tarifa Stripe según liquidación (<c>balance_transaction.fee</c>) y enlace a políticas Stripe.
/// </summary>
public sealed record ChatPaymentFeeReceiptPayload : ChatMessagePayload
{
    public ChatPaymentFeeReceiptPayload() => Type = "payment_fee_receipt";

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
