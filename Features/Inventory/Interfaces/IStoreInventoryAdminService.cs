using VibeTrade.Backend.Features.Inventory.Dtos;

namespace VibeTrade.Backend.Features.Inventory.Interfaces;

public interface IStoreInventoryAdminService
{
    Task<bool> ApproveProductAsync(string storeId, string productId, string userId, CancellationToken ct);
    Task<bool> RemoveProductFromCatalogAsync(string storeId, string productId, string userId, CancellationToken ct);
    Task<IReadOnlyList<StoreSupplierDto>?> ListSuppliersAsync(string storeId, string userId, CancellationToken ct);
    Task<StoreSupplierDto?> CreateSupplierAsync(string storeId, string userId, StoreSupplierCreateBody body, CancellationToken ct);
    Task<IReadOnlyList<StoreBannerDto>?> ListBannersAsync(string storeId, string? kind, bool activeOnly, CancellationToken ct);
    Task<IReadOnlyList<StoreBannerDto>?> ListPublicBannersAsync(string storeId, string kind, CancellationToken ct);
    Task<StoreBannerDto?> CreateBannerAsync(string storeId, string userId, StoreBannerCreateBody body, CancellationToken ct);
    Task<bool> PatchBannerAsync(string storeId, string bannerId, string userId, StoreBannerPatchBody body, CancellationToken ct);
    Task<bool> DeleteBannerAsync(string storeId, string bannerId, string userId, CancellationToken ct);
}
