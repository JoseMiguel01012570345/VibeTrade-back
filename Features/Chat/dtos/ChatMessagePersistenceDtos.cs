using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>
/// Estado de entrega/lectura del mensaje. Fechas asociadas en UTC en <see cref="Chat.Entities.ChatMessageRow"/>.
/// </summary>
public enum ChatMessageStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Read = 3,
    Error = 4,
}

/// <summary>
/// Reconocimientos de entrega/lectura por destinatario en hilos con más de 2 participantes.
/// </summary>
public sealed class ChatMessageGroupReceipts
{
    [JsonPropertyName("expectedRecipientIds")]
    public List<string> ExpectedRecipientIds { get; set; } = new();

    [JsonPropertyName("deliveredUserIds")]
    public List<string> DeliveredUserIds { get; set; } = new();

    [JsonPropertyName("readUserIds")]
    public List<string> ReadUserIds { get; set; } = new();
}
