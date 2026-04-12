namespace VibeTrade.Backend.Data.Entities;

/// <summary>Evento mínimo para alimentar recomendaciones por usuario/oferta.</summary>
public sealed class UserOfferInteractionRow
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string OfferId { get; set; } = "";

    /// <summary>`click`, `inquiry` o `chat_start`.</summary>
    public string EventType { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
