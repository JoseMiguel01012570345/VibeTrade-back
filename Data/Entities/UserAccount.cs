namespace VibeTrade.Backend.Data.Entities;

/// <summary>Cuenta de usuario en plataforma (perfil persistido; la sesión OTP sigue en memoria).</summary>
public sealed class UserAccount
{
    public string Id { get; set; } = "";

    /// <summary>Dígitos normalizados para login; único cuando está presente.</summary>
    public string? PhoneDigits { get; set; }

    public string? AvatarUrl { get; set; }

    /// <summary>Nombre para mostrar.</summary>
    public string DisplayName { get; set; } = "";

    public string? Email { get; set; }

    /// <summary>Teléfono formateado para UI.</summary>
    public string? PhoneDisplay { get; set; }

    public string? Instagram { get; set; }

    public string? Telegram { get; set; }

    /// <summary>Cuenta de X (Twitter).</summary>
    public string? XAccount { get; set; }

    /// <summary>Identificador de cliente en Stripe (cus_...).</summary>
    public string? StripeCustomerId { get; set; }

    public int TrustScore { get; set; } = 50;

    /// <summary>Ids de producto/servicio guardados (jsonb: <c>SavedOfferIdsJson</c>).</summary>
    public List<string> SavedOfferIds { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<StoreRow> Stores { get; set; } = new List<StoreRow>();
}
