using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>
/// Reconocimientos de entrega/lectura por destinatario cuando el hilo tiene más de 2 participantes
/// (comprador, vendedor, transportista(s)). Persistido en <see cref="VibeTrade.Backend.Data.Entities.ChatMessageRow.GroupReceiptsJson"/>.
/// </summary>
public sealed class ChatMessageGroupReceipts
{
    /// <summary>Destinatarios al enviar; usado para saber cuándo está completo el reparto o la lectura.</summary>
    [JsonPropertyName("expectedRecipientIds")]
    public List<string> ExpectedRecipientIds { get; set; } = new();

    [JsonPropertyName("deliveredUserIds")]
    public List<string> DeliveredUserIds { get; set; } = new();

    [JsonPropertyName("readUserIds")]
    public List<string> ReadUserIds { get; set; } = new();
}
