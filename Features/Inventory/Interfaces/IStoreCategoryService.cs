using VibeTrade.Backend.Features.Inventory.Dtos;

namespace VibeTrade.Backend.Features.Inventory.Interfaces;

public interface IStoreCategoryService
{
    Task<IReadOnlyList<StoreCategoryDto>?> ListAsync(string storeId, CancellationToken ct);
    Task<StoreCategoryDto?> CreateAsync(string storeId, string userId, StoreCategoryCreateBody body, CancellationToken ct);
    Task<bool> PatchAsync(string storeId, string categoryId, string userId, StoreCategoryPatchBody body, CancellationToken ct);
    Task<bool> DeleteAsync(string storeId, string categoryId, string userId, CancellationToken ct);
    Task EnsureCategoriesFromLegacyProductsAsync(string storeId, CancellationToken ct);
}
