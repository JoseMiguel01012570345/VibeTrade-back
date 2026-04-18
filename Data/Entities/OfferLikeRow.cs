namespace VibeTrade.Backend.Data.Entities;

/// <summary>Like de usuario o invitado (<c>u:…</c> / <c>g:…</c>) a una oferta de catálogo.</summary>
public sealed class OfferLikeRow
{
    public string Id { get; set; } = "";

    public string OfferId { get; set; } = "";

    /// <summary><c>u:</c> + userId o <c>g:</c> + guestId.</summary>
    public string LikerKey { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
