namespace VibeTrade.Backend.Features.Inventory.Entities;

/// <summary>Categoría jerárquica del catálogo de una tienda.</summary>
public sealed class StoreCategoryRow
{
    public string Id { get; set; } = "";

    public string StoreId { get; set; } = "";

    public StoreRow Store { get; set; } = null!;

    public string Name { get; set; } = "";

    public string? ParentCategoryId { get; set; }

    public StoreCategoryRow? Parent { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
