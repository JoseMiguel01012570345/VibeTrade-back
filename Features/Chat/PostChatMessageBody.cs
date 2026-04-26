using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Cuerpo del POST de mensaje al hilo. <see cref="Type"/> elige el subconjunto de campos
/// (p. ej. <c>text</c> con <c>text</c>, <c>offerQaId</c>, <c>replyToIds</c>).
/// </summary>
public sealed class PostChatMessageBody
{
    public required string Type { get; init; }

    public IReadOnlyList<string>? ReplyToIds { get; init; }

    // type=text
    public string? Text { get; init; }
    public string? OfferQaId { get; init; }

    // type=audio
    public string? Url { get; init; }
    public int? Seconds { get; init; }

    // type=image
    public IReadOnlyList<ChatImageDto>? Images { get; init; }
    public string? Caption { get; init; }
    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }

    // type=doc (mismo <c>caption</c> opcional que otras variantes)
    public string? Name { get; init; }
    public string? Size { get; init; }
    public string? Kind { get; init; }

    // type=docs
    public IReadOnlyList<ChatDocumentDto>? Documents { get; init; }
}
