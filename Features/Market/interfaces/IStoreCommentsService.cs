namespace VibeTrade.Backend.Features.Market.Interfaces;

/// <summary>
/// Comentarios públicos a nivel de tienda (tablero tipo Q&amp;A): los clientes publican
/// comentarios/preguntas y el dueño responde en el hilo. Mismo formato de datos que el
/// Q&amp;A por-oferta, pero persistido en la columna jsonb <c>CommentsJson</c> de la tienda.
/// </summary>
public interface IStoreCommentsService
{
    /// <summary>Lista los comentarios de la tienda enriquecidos con likes; <c>null</c> si la tienda no existe.</summary>
    Task<IReadOnlyList<OfferQaItemResponseDto>?> GetStoreCommentsAsync(
        string storeId,
        string? likerKey,
        CancellationToken cancellationToken = default);

    /// <summary>Agrega un comentario (o respuesta si <paramref name="parentId"/>) y lo devuelve; <c>null</c> si la tienda no existe.</summary>
    Task<OfferQaComment?> AppendStoreCommentAsync(
        string storeId,
        string text,
        string? parentId,
        string authorId,
        string authorName,
        int authorTrust,
        long? createdAtMs,
        CancellationToken cancellationToken = default);

    /// <summary>Alterna el like de un comentario para el visitante autenticado y devuelve el conteo actualizado.</summary>
    Task<(bool Liked, int LikeCount)> ToggleStoreCommentLikeAsync(
        string storeId,
        string commentId,
        string likerKey,
        CancellationToken cancellationToken = default);
}
