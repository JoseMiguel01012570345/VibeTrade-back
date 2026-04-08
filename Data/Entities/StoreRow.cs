namespace VibeTrade.Backend.Data.Entities;

/// <summary>Tienda: un usuario puede tener varias.</summary>
public sealed class StoreRow
{
    public string Id { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public UserAccount Owner { get; set; } = null!;

    public string Name { get; set; } = "";

    /// <summary>lower(trim(collapse spaces)), alineado al cliente; null si el nombre queda vacío (no aplica unicidad).</summary>
    public string? NormalizedName { get; set; }

    public bool Verified { get; set; }

    public bool TransportIncluded { get; set; }

    public int TrustScore { get; set; } = 50;

    public string? AvatarUrl { get; set; }

    /// <summary>Categorías (JSON array de strings en jsonb).</summary>
    public string CategoriesJson { get; set; } = "[]";

    /// <summary>Descripción del catálogo (pitch).</summary>
    public string Pitch { get; set; } = "";

    /// <summary>Fecha de alta en la plataforma (epoch ms, alineado al cliente).</summary>
    public long JoinedAtMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<StoreProductRow> Products { get; set; } = new List<StoreProductRow>();

    public ICollection<StoreServiceRow> Services { get; set; } = new List<StoreServiceRow>();
}
