using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Trust;

public static class TrustAwardHelper
{
    public static async Task<bool> TryAwardUserTrustAsync(
        AppDbContext db,
        ITrustScoreLedgerService trustLedger,
        string userId,
        int delta,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2 || delta == 0)
            return false;

        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == uid, cancellationToken)
            .ConfigureAwait(false);
        if (acc is null)
            return false;

        acc.TrustScore = Math.Max(-10_000, acc.TrustScore + delta);
        trustLedger.StageEntry(
            TrustLedgerSubjects.User,
            uid,
            delta,
            acc.TrustScore,
            reason);
        return true;
    }
}
