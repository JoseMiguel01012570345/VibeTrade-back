namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>
/// Cuerpo del POST de mensaje al hilo. El servidor arma un <see cref="ChatUnifiedMessagePayload"/> a partir de los
/// campos presentes (texto, url/segundos, imágenes, documentos, citas).
/// </summary>
public sealed class PostChatMessageBody
{
    public IReadOnlyList<string>? ReplyToIds { get; init; }

    public string? Text { get; init; }
    public string? OfferQaId { get; init; }

    public string? Url { get; init; }
    public int? Seconds { get; init; }

    public IReadOnlyList<ChatImageDto>? Images { get; init; }
    public string? Caption { get; init; }
    public ChatEmbeddedAudioDto? EmbeddedAudio { get; init; }

    public string? Name { get; init; }
    public string? Size { get; init; }
    public string? Kind { get; init; }

    public IReadOnlyList<ChatDocumentDto>? Documents { get; init; }
}
