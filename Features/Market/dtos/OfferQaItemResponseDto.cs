using System.Text.Json.Serialization;

namespace VibeTrade.Backend.Features.Market.Dtos;

/// <summary>Ítem de QA con campos de engagement para respuestas API (no persiste en jsonb).</summary>
public sealed class OfferQaItemResponseDto
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Question { get; set; }
    public string? ParentId { get; set; }
    public OfferQaAuthorSnapshot? AskedBy { get; set; }
    public OfferQaAuthorSnapshot? Author { get; set; }
    public long CreatedAt { get; set; }
    public string? Answer { get; set; }
    [JsonPropertyName("likeCount")]
    public int LikeCount { get; set; }
    [JsonPropertyName("viewerLiked")]
    public bool ViewerLiked { get; set; }
}
