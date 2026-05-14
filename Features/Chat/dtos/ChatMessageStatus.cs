namespace VibeTrade.Backend.Features.Chat.Dtos;

/// <summary>
/// Estado de entrega/lectura del mensaje. Fechas asociadas en UTC en <see cref="VibeTrade.Backend.Data.Entities.ChatMessageRow"/>.
/// </summary>
public enum ChatMessageStatus
{
    /// <summary>Solo cliente (optimistic); no persistir en DB.</summary>
    Pending = 0,

    /// <summary>Persistido en servidor.</summary>
    Sent = 1,

    /// <summary>Entregado al dispositivo del destinatario (vía realtime).</summary>
    Delivered = 2,

    /// <summary>Leído por el destinatario.</summary>
    Read = 3,

    /// <summary>Error al enviar o procesar (p. ej. cliente).</summary>
    Error = 4,
}
