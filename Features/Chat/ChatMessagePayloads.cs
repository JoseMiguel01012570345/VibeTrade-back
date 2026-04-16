using System;
using System.Collections.Generic;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Shape of the JSON payload stored in <see cref="Data.Entities.ChatMessageRow.PayloadJson"/>.
/// Always serialized with System.Text.Json using JsonSerializerDefaults.Web (camelCase).
/// </summary>
public abstract record ChatMessagePayload
{
    /// <summary>
    /// Discriminator for the concrete payload type.
    /// Examples: "text", "image", "audio", "doc", "docs", "agreement", "certificate", "system_text".
    /// </summary>
    public string Type { get; init; } = "";
}

public sealed record ReplyQuoteDto
{
    public required string MessageId { get; init; }

    /// <summary>"me" | "other" | "system".</summary>
    public required string From { get; init; }

    /// <summary>Plain-text preview of the quoted message.</summary>
    public required string Text { get; init; }

    /// <summary>UTC timestamp of the original message.</summary>
    public required DateTimeOffset AtUtc { get; init; }
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
    public (string Url, int Seconds)? EmbeddedAudio { get; init; }

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
    public (string Url, int Seconds)? EmbeddedAudio { get; init; }

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

    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>e.g. "pending", "accepted", "rejected".</summary>
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

