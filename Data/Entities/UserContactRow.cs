namespace VibeTrade.Backend.Data.Entities;

/// <summary>Contacto guardado por un usuario: referencia a otra cuenta de la plataforma.</summary>
public sealed class UserContactRow
{
    public string Id { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string ContactUserId { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Borrado lógico; null = contacto visible en la agenda.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
