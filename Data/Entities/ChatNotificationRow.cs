namespace VibeTrade.Backend.Data.Entities;

/// <summary>Notificación in-app por mensaje en chat o comentario en oferta (destinatario).</summary>
public sealed class ChatNotificationRow
{
    public string Id { get; set; } = "";

    public string RecipientUserId { get; set; } = "";

    /// <summary>Nulo si la notificación es solo por comentario en oferta (<see cref="OfferId"/>).</summary>
    public string? ThreadId { get; set; }

    /// <summary>Nulo si la notificación no está ligada a un mensaje de chat (p. ej. comentario en ficha).</summary>
    public string? MessageId { get; set; }

    /// <summary>Oferta asociada cuando el aviso es por comentario público (enlace a <c>/offer/:id</c>).</summary>
    public string? OfferId { get; set; }

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

    /// <summary>
    /// <c>offer_comment</c>, <c>offer_like</c>, <c>qa_comment_like</c>, <c>route_tramo_subscribe</c>, <c>route_tramo_subscribe_accepted</c>, <c>route_tramo_subscribe_rejected</c>, <c>route_sheet_presel</c>, <c>route_sheet_presel_decl</c> (máx. 32 caracteres en BD); nulo en avisos de chat por hilo.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>JSON opcional (p. ej. deep link a panel de suscriptores en chat).</summary>
    public string? MetaJson { get; set; }
}
