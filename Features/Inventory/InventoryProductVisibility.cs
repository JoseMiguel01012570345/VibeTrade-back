using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Entities;

namespace VibeTrade.Backend.Features.Inventory;

/// <summary>Reglas de visibilidad pública y banners del inventario por tienda.</summary>
public static class InventoryProductVisibility
{
    public static bool IsPubliclyVisible(StoreProductRow p) =>
        p.Published
        && !p.PendingApproval
        && p.DeletedAtUtc is null
        && (p.StockQuantity is null || p.StockQuantity > 0);

    public static IQueryable<StoreProductRow> WherePubliclyVisible(this IQueryable<StoreProductRow> q) =>
        q.Where(p =>
            p.Published
            && !p.PendingApproval
            && p.DeletedAtUtc == null
            && (p.StockQuantity == null || p.StockQuantity > 0));

    public static StoreBannerDto ToBannerDto(StoreBannerRow b) =>
        new(b.Id, b.StoreId, b.Kind == StoreBannerKind.Main ? "main" : "secondary",
            b.SortOrder, b.MediaUrl, b.Active);

    public static StoreBannerKind? ParseBannerKind(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "main" => StoreBannerKind.Main,
        "secondary" => StoreBannerKind.Secondary,
        _ => null,
    };
}
