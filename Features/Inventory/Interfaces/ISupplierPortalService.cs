using VibeTrade.Backend.Features.Inventory.Dtos;
using VibeTrade.Backend.Features.Inventory.Entities;

namespace VibeTrade.Backend.Features.Inventory.Interfaces;

public interface ISupplierPortalService
{
    Task<StoreSupplierRow?> AuthenticateAsync(string username, string password, CancellationToken ct);
    Task<SupplierPortalMeDto?> GetMeAsync(string supplierId, CancellationToken ct);
    Task<SupplierPortalDashboardDto?> GetDashboardAsync(
        string supplierId,
        int transactionsPage,
        int transactionsPageSize,
        CancellationToken ct);
    Task<SupplierPortalInventoryBulkUpdateResult> BulkUpdateInventoryAsync(
        string supplierId,
        SupplierPortalInventoryBulkUpdateRequest request,
        CancellationToken ct);
}
