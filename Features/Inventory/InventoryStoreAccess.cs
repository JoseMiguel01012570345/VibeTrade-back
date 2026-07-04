using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Inventory;

internal static class InventoryStoreAccess
{
    public static Task<bool> StoreExistsAsync(AppDbContext db, string storeId, CancellationToken ct) =>
        db.Stores.AsNoTracking().AnyAsync(s => s.Id == storeId && s.DeletedAtUtc == null, ct);

    public static async Task<bool> IsStoreOwnerAsync(
        AppDbContext db,
        string storeId,
        string userId,
        CancellationToken ct)
    {
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId, ct);
        return store is not null && store.OwnerUserId == userId;
    }
}
