namespace VibeTrade.Backend.Features.Inventory.Entities;

public enum StoreBannerKind
{
    Main = 0,
    Secondary = 1,
}

/// <summary>Banner promocional de la vitrina de una tienda.</summary>
public sealed class StoreBannerRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public StoreBannerKind Kind { get; set; }

    public int SortOrder { get; set; }

    public string MediaUrl { get; set; } = "";

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
