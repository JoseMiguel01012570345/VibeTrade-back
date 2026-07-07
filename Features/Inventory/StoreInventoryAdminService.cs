using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Entities;
using VibeTrade.Backend.Features.Inventory.Interfaces;

namespace VibeTrade.Backend.Features.Inventory;

public sealed class StoreInventoryAdminService(AppDbContext db) : IStoreInventoryAdminService
{
    public async Task<bool> ApproveProductAsync(string storeId, string productId, string userId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var row = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, ct);
        if (row is null) return false;
        row.PendingApproval = false;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveProductFromCatalogAsync(string storeId, string productId, string userId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var row = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, ct);
        if (row is null) return false;
        row.Published = false;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<StoreSupplierDto>?> ListSuppliersAsync(string storeId, string userId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return null;
        return await db.StoreSuppliers.AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderBy(s => s.BusinessName)
            .Select(s => new StoreSupplierDto(
                s.Id, s.StoreId, s.BusinessName, s.PortalUsername, s.Active,
                s.PlatformDebtAmount, s.PlatformDebtCurrencyCode))
            .ToListAsync(ct);
    }

    public async Task<StoreSupplierDto?> CreateSupplierAsync(
        string storeId, string userId, StoreSupplierCreateBody body, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return null;
        var name = (body.BusinessName ?? "").Trim();
        var username = (body.PortalUsername ?? "").Trim().ToLowerInvariant();
        var password = body.Password ?? "";
        if (name.Length < 1 || username.Length < 3 || password.Length < 6) return null;
        var now = DateTimeOffset.UtcNow;
        var row = new StoreSupplierRow
        {
            Id = $"sup_{Guid.NewGuid():N}"[..24],
            StoreId = storeId,
            BusinessName = name,
            PortalUsername = username,
            PasswordHash = InventorySupplierPassword.Hash(password),
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StoreSuppliers.Add(row);
        await db.SaveChangesAsync(ct);
        return new StoreSupplierDto(row.Id, row.StoreId, row.BusinessName, row.PortalUsername,
            row.Active, row.PlatformDebtAmount, row.PlatformDebtCurrencyCode);
    }

    public async Task<IReadOnlyList<StoreBannerDto>?> ListBannersAsync(
        string storeId, string? kind, bool activeOnly, CancellationToken ct)
    {
        var q = db.StoreBanners.AsNoTracking().Where(b => b.StoreId == storeId);
        if (activeOnly) q = q.Where(b => b.Active);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            var k = InventoryProductVisibility.ParseBannerKind(kind);
            if (k is not null) q = q.Where(b => b.Kind == k);
        }
        return await q.OrderBy(b => b.SortOrder).ThenBy(b => b.CreatedAt)
            .Select(b => InventoryProductVisibility.ToBannerDto(b))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StoreBannerDto>?> ListPublicBannersAsync(
        string storeId, string kind, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.StoreExistsAsync(db, storeId, ct))
            return null;
        var k = InventoryProductVisibility.ParseBannerKind(kind);
        if (k is null) return Array.Empty<StoreBannerDto>();
        return await db.StoreBanners.AsNoTracking()
            .Where(b => b.StoreId == storeId && b.Active && b.Kind == k)
            .OrderBy(b => b.SortOrder)
            .Select(b => InventoryProductVisibility.ToBannerDto(b))
            .ToListAsync(ct);
    }

    public async Task<StoreBannerDto?> CreateBannerAsync(
        string storeId, string userId, StoreBannerCreateBody body, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return null;
        var k = InventoryProductVisibility.ParseBannerKind(body.Kind);
        if (k is null || string.IsNullOrWhiteSpace(body.MediaUrl)) return null;
        var now = DateTimeOffset.UtcNow;
        var row = new StoreBannerRow
        {
            Id = $"ban_{Guid.NewGuid():N}"[..24],
            StoreId = storeId,
            Kind = k.Value,
            SortOrder = body.SortOrder ?? 0,
            MediaUrl = body.MediaUrl.Trim(),
            Active = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StoreBanners.Add(row);
        await db.SaveChangesAsync(ct);
        return InventoryProductVisibility.ToBannerDto(row);
    }

    public async Task<bool> PatchBannerAsync(
        string storeId, string bannerId, string userId, StoreBannerPatchBody body, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var row = await db.StoreBanners.FirstOrDefaultAsync(b => b.Id == bannerId && b.StoreId == storeId, ct);
        if (row is null) return false;
        if (body.Active.HasValue) row.Active = body.Active.Value;
        if (body.SortOrder.HasValue) row.SortOrder = body.SortOrder.Value;
        if (!string.IsNullOrWhiteSpace(body.MediaUrl)) row.MediaUrl = body.MediaUrl.Trim();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteBannerAsync(string storeId, string bannerId, string userId, CancellationToken ct)
    {
        if (!await InventoryStoreAccess.IsStoreOwnerAsync(db, storeId, userId, ct)) return false;
        var row = await db.StoreBanners.FirstOrDefaultAsync(b => b.Id == bannerId && b.StoreId == storeId, ct);
        if (row is null) return false;
        db.StoreBanners.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
