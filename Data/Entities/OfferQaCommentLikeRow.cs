namespace VibeTrade.Backend.Data.Entities;

/// <summary>Like a un comentario público (<c>id</c> en <c>OfferQaJson</c>).</summary>
public sealed class OfferQaCommentLikeRow
{
    public string Id { get; set; } = "";

    public string OfferId { get; set; } = "";

    public string QaCommentId { get; set; } = "";

    public string LikerKey { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
