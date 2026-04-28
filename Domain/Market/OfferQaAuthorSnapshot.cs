namespace VibeTrade.Backend.Domain.Market;

/// <summary>Autor de un comentario QA (persistido en jsonb).</summary>
public sealed class OfferQaAuthorSnapshot
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int TrustScore { get; set; }
}
