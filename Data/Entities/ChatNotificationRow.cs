namespace VibeTrade.Backend.Data.Entities;

/// <summary>Notificación in-app por mensaje nuevo en chat (destinatario).</summary>
public sealed class ChatNotificationRow
{
    public string Id { get; set; } = "";

    public string RecipientUserId { get; set; } = "";

    public string ThreadId { get; set; } = "";

    public string MessageId { get; set; } = "";

    /// <summary>Vista previa del texto (para listado).</summary>
    public string MessagePreview { get; set; } = "";

    /// <summary>Autor en nombre de la tienda (nombre comercial).</summary>
    public string AuthorStoreName { get; set; } = "";

    public int AuthorTrustScore { get; set; }

    public string SenderUserId { get; set; } = "";

    /// <summary>UTC: creación.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC: lectura.</summary>
    public DateTimeOffset? ReadAtUtc { get; set; }
}
