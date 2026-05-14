namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>
/// Un ítem del array de comentarios/preguntas públicas de una oferta (columna jsonb <c>OfferQaJson</c>).
/// Alineado con el contrato del cliente (<c>qa</c> en ofertas). Los campos <c>likeCount</c> / <c>viewerLiked</c>
/// solo existen en respuestas API enriquecidas, no en persistencia.
/// </summary>
public sealed class OfferQaComment
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public string? Question { get; set; }

    public string? ParentId { get; set; }

    public OfferQaAuthorSnapshot? AskedBy { get; set; }

    public OfferQaAuthorSnapshot? Author { get; set; }

    /// <summary>Unix ms.</summary>
    public long CreatedAt { get; set; }

    /// <summary>Respuesta del vendedor (cuando existe).</summary>
    public string? Answer { get; set; }
}

/// <summary>Autor de un comentario QA (persistido en jsonb).</summary>
public sealed class OfferQaAuthorSnapshot
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int TrustScore { get; set; }
}
