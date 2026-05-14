namespace VibeTrade.Backend.Data.Entities;

/// <summary>Mensaje persistido; fechas siempre en UTC.</summary>
public sealed class ChatMessageRow
{
    public string Id { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public ChatThreadRow Thread { get; set; } = null!;

    public string SenderUserId { get; set; } = "";

    /// <summary>Contenido del mensaje; persistido en columna <c>PayloadJson</c> (jsonb).</summary>
    public ChatMessagePayload Payload { get; set; } = new ChatUnifiedMessagePayload { Text = "" };

    public ChatMessageStatus Status { get; set; } = ChatMessageStatus.Sent;

    /// <summary>
    /// JSON (ver <see cref="ChatMessageGroupReceipts"/>). Sólo grupos; si es null, aplica <see cref="Status"/> 1:1.
    /// </summary>
    public string? GroupReceiptsJson { get; set; }

    /// <summary>UTC: instante de envío.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC: último cambio de estado (entrega/lectura).</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>UTC: borrado lógico (p. ej. con el hilo); null = activo.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
