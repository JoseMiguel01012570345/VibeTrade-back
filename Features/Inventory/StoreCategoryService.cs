using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Entities;
using VibeTrade.Backend.Features.Inventory.Interfaces;

namespace VibeTrade.Backend.Features.Inventory;

public sealed class StoreCategoryService(AppDbContext db) : IStoreCategoryService
{
    public async Task<IReadOnlyList<StoreCategoryDto>?> ListAsync(string storeId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.StoreExistsAsync(db, storeId, ct)) return null;
        await EnsureCategoriesFromLegacyProductsAsync(storeId, ct);
        return await db.StoreCategories.AsNoTracking()
            .Where(c => c.StoreId == storeId && c.Active)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new StoreCategoryDto(c.Id, c.Name, c.ParentCategoryId))
            .ToListAsync(ct);
    }

    public async Task<StoreCategoryDto?> CreateAsync(
        string storeId, string userId, StoreCategoryCreateBody body, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return null;
        var name = (body.Name ?? "").Trim();
        if (name.Length < 1) return null;
        var now = DateTimeOffset.UtcNow;
        var row = new StoreCategoryRow
        {
            Id = $"cat_{Guid.NewGuid():N}"[..24],
            StoreId = storeId,
            Name = name,
            ParentCategoryId = string.IsNullOrWhiteSpace(body.ParentCategoryId) ? null : body.ParentCategoryId.Trim(),
            SortOrder = 0,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StoreCategories.Add(row);
        await db.SaveChangesAsync(ct);
        return new StoreCategoryDto(row.Id, row.Name, row.ParentCategoryId);
    }

    public async Task<bool> PatchAsync(
        string storeId, string categoryId, string userId, StoreCategoryPatchBody body, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var row = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.StoreId == storeId, ct);
        if (row is null) return false;
        if (!string.IsNullOrWhiteSpace(body.Name)) row.Name = body.Name.Trim();
        if (body.Active.HasValue) row.Active = body.Active.Value;
        if (body.SortOrder.HasValue) row.SortOrder = body.SortOrder.Value;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string storeId, string categoryId, string userId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var hasChildren = await db.StoreCategories.AnyAsync(c => c.ParentCategoryId == categoryId, ct);
        if (hasChildren) return false;
        var row = await db.StoreCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.StoreId == storeId, ct);
        if (row is null) return false;
        db.StoreCategories.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task EnsureCategoriesFromLegacyProductsAsync(string storeId, CancellationToken ct)
    {
        if (await db.StoreCategories.AnyAsync(c => c.StoreId == storeId, ct))
            return;

        var names = await db.StoreProducts.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.DeletedAtUtc == null)
            .Select(p => p.Category.Trim())
            .Where(c => c.Length > 0)
            .Distinct()
            .ToListAsync(ct);

        if (names.Count == 0)
            names.Add("General");

        var now = DateTimeOffset.UtcNow;
        foreach (var name in names)
        {
            var id = $"cat_{Guid.NewGuid():N}"[..24];
            db.StoreCategories.Add(new StoreCategoryRow
            {
                Id = id,
                StoreId = storeId,
                Name = name,
                ParentCategoryId = null,
                SortOrder = 0,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

            var products = await db.StoreProducts
                .Where(p => p.StoreId == storeId && p.Category.Trim() == name && p.DeletedAtUtc == null)
                .ToListAsync(ct);
            foreach (var p in products)
            {
                if (p.CategoryIds.Count == 0)
                    p.CategoryIds = new List<string> { id };
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
