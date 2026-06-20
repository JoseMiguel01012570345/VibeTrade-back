using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Trust;

/// <summary>Resuelve la tienda que brinda el servicio de transporte en un tramo confirmado.</summary>
public static class TransportTramoTrustResolver
{
    public static async Task<string?> ResolveTransportProviderStoreIdAsync(
        AppDbContext db,
        string threadId,
        string routeSheetId,
        string routeStopId,
        string carrierUserId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        var sid = (routeStopId ?? "").Trim();
        var uid = (carrierUserId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1 || sid.Length < 1 || uid.Length < 2)
            return null;

        var sub = await db.RouteTramoSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid
                    && x.RouteSheetId == rsid
                    && x.StopId == sid
                    && x.CarrierUserId == uid
                    && x.Status == "confirmed",
                cancellationToken)
            .ConfigureAwait(false);

        var svcId = (sub?.StoreServiceId ?? "").Trim();
        if (svcId.Length >= 2)
        {
            var storeFromSvc = await db.StoreServices.AsNoTracking()
                .Where(s => s.Id == svcId && s.DeletedAtUtc == null)
                .Select(s => s.StoreId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(storeFromSvc))
                return storeFromSvc.Trim();
        }

        return await db.StoreServices.AsNoTracking()
            .Where(s => s.DeletedAtUtc == null)
            .Join(
                db.Stores.AsNoTracking().Where(st => st.DeletedAtUtc == null && st.OwnerUserId == uid),
                s => s.StoreId,
                st => st.Id,
                (s, _) => s)
            .OrderByDescending(s => s.Published == true)
            .Select(s => s.StoreId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
