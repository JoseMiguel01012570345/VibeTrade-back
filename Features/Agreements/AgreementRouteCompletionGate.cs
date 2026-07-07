using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Agreements;

public static class AgreementRouteCompletionGate
{
    public static async Task<bool> IsLinkedRouteSheetDeliveredAsync(
        AppDbContext db,
        string threadId,
        string routeSheetId,
        CancellationToken cancellationToken = default)
    {
        var tid = (threadId ?? "").Trim();
        var rsid = (routeSheetId ?? "").Trim();
        if (tid.Length < 4 || rsid.Length < 1)
            return false;

        var row = await db.ChatRouteSheets.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ThreadId == tid && x.RouteSheetId == rsid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        return string.Equals(
            (row.Payload?.Estado ?? "").Trim(),
            "entregada",
            StringComparison.OrdinalIgnoreCase);
    }
}
