namespace VibeTrade.Backend.Features.Market.Entities;

/// <summary>Like a un comentario público de tienda (<c>id</c> en <c>CommentsJson</c> de la tienda).</summary>
public sealed class StoreCommentLikeRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public string CommentId { get; set; } = "";

    public string LikerKey { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
