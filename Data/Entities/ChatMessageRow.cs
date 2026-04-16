using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Data.Entities;

/// <summary>Mensaje persistido; fechas siempre en UTC.</summary>
public sealed class ChatMessageRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public ChatThreadRow Thread { get; set; } = null!;

    public string SenderUserId { get; set; } = "";

    /// <summary>JSON con forma de cliente: type text/image/audio/… + campos.</summary>
    public string PayloadJson { get; set; } = "{}";

    public ChatMessageStatus Status { get; set; } = ChatMessageStatus.Sent;

    /// <summary>UTC: instante de envío.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC: último cambio de estado (entrega/lectura).</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>UTC: borrado lógico (p. ej. con el hilo); null = activo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
