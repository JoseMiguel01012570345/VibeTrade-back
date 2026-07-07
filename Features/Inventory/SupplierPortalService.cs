using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Entities;
using VibeTrade.Backend.Features.Inventory.Interfaces;

namespace VibeTrade.Backend.Features.Inventory;

public sealed class SupplierPortalService(AppDbContext db) : ISupplierPortalService
{
    public async Task<StoreSupplierRow?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        var u = (username ?? "").Trim().ToLowerInvariant();
        var supplier = await db.StoreSuppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PortalUsername == u && s.Active, ct);
        if (supplier is null) return null;
        return InventorySupplierPassword.Verify(password, supplier.PasswordHash) ? supplier : null;
    }

    public async Task<SupplierPortalMeDto?> GetMeAsync(string supplierId, CancellationToken ct)
    {
        var s = await db.StoreSuppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == supplierId, ct);
        return s is null ? null : new SupplierPortalMeDto(s.BusinessName, s.PortalUsername);
    }

    public async Task<SupplierPortalDashboardDto?> GetDashboardAsync(
        string supplierId,
        int transactionsPage,
        int transactionsPageSize,
        CancellationToken ct)
    {
        var supplier = await db.StoreSuppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null) return null;

        var page = Math.Max(1, transactionsPage);
        var pageSize = Math.Clamp(transactionsPageSize, 1, 100);

        var products = await db.StoreProducts.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.DeletedAtUtc == null)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var categories = await db.StoreCategories.AsNoTracking()
            .Where(c => c.StoreId == supplier.StoreId && c.Active)
            .OrderBy(c => c.Name)
            .Select(c => new SupplierPortalCategoryOptionDto(c.Id, c.Name))
            .ToListAsync(ct);

        var catById = categories.ToDictionary(c => c.Id, c => c.Name);

        var inventory = products.Select(p => MapInventoryRow(p, catById)).ToList();

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.StoreId == supplier.StoreId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var txTotal = orders.Count;
        var txItems = orders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new SupplierPortalTransactionRowDto(
                "order",
                o.PublicNumber ?? o.Id,
                o.CreatedAtUtc,
                o.Total,
                o.CurrencyCode ?? "USD",
                $"{o.CustomerFirstName} {o.CustomerLastName}".Trim()))
            .ToList();

        var debt = supplier.PlatformDebtAmount;
        var kpis = new SupplierPortalDashboardKpisDto(
            debt,
            Math.Max(0m, debt * 0.6m),
            Math.Max(0m, debt * 0.4m),
            supplier.PlatformDebtCurrencyCode);

        return new SupplierPortalDashboardDto(
            kpis,
            new SupplierPortalPagedTransactionsDto(txItems, txTotal, page, pageSize),
            new SupplierPortalInventoryDto(inventory, categories));
    }

    public async Task<SupplierPortalInventoryBulkUpdateResult> BulkUpdateInventoryAsync(
        string supplierId,
        SupplierPortalInventoryBulkUpdateRequest request,
        CancellationToken ct)
    {
        var errors = new List<SupplierPortalInventoryBulkUpdateError>();
        var updated = 0;
        foreach (var item in request.Items ?? Array.Empty<SupplierPortalInventoryBulkUpdateItem>())
        {
            var row = await db.StoreProducts.FirstOrDefaultAsync(
                p => p.Id == item.Id && p.SupplierId == supplierId, ct);
            if (row is null)
            {
                errors.Add(new SupplierPortalInventoryBulkUpdateError(item.Id, "Producto no encontrado en tu inventario."));
                continue;
            }
            row.StockQuantity = Math.Max(0, item.Stock);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return new SupplierPortalInventoryBulkUpdateResult(updated, errors);
    }

    private static SupplierPortalInventoryRowDto MapInventoryRow(
        StoreProductRow p,
        IReadOnlyDictionary<string, string> catById)
    {
        var ids = p.CategoryIds ?? new List<string>();
        var primary = ids.Count > 0 ? ids[0] : null;
        var catName = primary is not null && catById.TryGetValue(primary, out var n) ? n : p.Category;
        var photo = p.PhotoUrls.Count > 0 ? p.PhotoUrls[0] : null;
        return new SupplierPortalInventoryRowDto(
            p.Id,
            p.Name,
            p.ShortDescription,
            $"#VT-{p.Id.Replace("-", "")[..6].ToUpperInvariant()}",
            catName,
            primary ?? "",
            ids,
            decimal.TryParse(p.Price, out var price) ? price : 0m,
            p.MonedaPrecio ?? "USD",
            p.StockQuantity ?? 0,
            p.PendingApproval,
            InventoryProductVisibility.IsPubliclyVisible(p),
            photo);
    }
}
