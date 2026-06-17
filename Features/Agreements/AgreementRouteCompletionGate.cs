using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

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

    public static async Task<(bool Ok, string? ErrorCode)> ValidateMerchandiseEvidenceSubmitAsync(
        AppDbContext db,
        string threadId,
        string agreementId,
        bool submit,
        CancellationToken cancellationToken = default)
    {
        if (!submit)
            return (true, null);

        var aid = (agreementId ?? "").Trim();
        var tid = (threadId ?? "").Trim();
        if (aid.Length < 8 || tid.Length < 4)
            return (true, null);

        var ag = await db.TradeAgreements.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == aid && x.ThreadId == tid && x.DeletedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);
        var rsid = (ag?.RouteSheetId ?? "").Trim();
        if (rsid.Length == 0)
            return (true, null);

        if (await IsLinkedRouteSheetDeliveredAsync(db, tid, rsid, cancellationToken).ConfigureAwait(false))
            return (true, null);

        return (false, "route_not_delivered");
    }
}
