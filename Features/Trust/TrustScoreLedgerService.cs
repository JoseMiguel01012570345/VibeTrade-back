using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Trust.GetTrustLedger;

namespace VibeTrade.Backend.Features.Trust;

public sealed class TrustScoreLedgerService(AppDbContext db, IMediator mediator) : ITrustScoreLedgerService
{
    private static string NewId() => "thl_" + Guid.NewGuid().ToString("N")[..16];

    public void StageEntry(
        string subjectType,
        string subjectId,
        int delta,
        int balanceAfter,
        string reason)
    {
        var st = (subjectType ?? "").Trim();
        var sid = (subjectId ?? "").Trim();
        if (st.Length < 2 || sid.Length < 2 || delta == 0)
            return;
        var r = (reason ?? "").Trim();
        if (r.Length > 512)
            r = r[..512];
        if (r.Length == 0)
            r = "—";
        db.TrustScoreLedgerRows.Add(new TrustScoreLedgerRow
        {
            Id = NewId(),
            SubjectType = st,
            SubjectId = sid,
            Delta = delta,
            BalanceAfter = balanceAfter,
            Reason = r,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public Task<IReadOnlyList<TrustHistoryItemDto>> ListForSubjectAsync(
        string subjectType,
        string subjectId,
        int limit,
        CancellationToken cancellationToken = default) =>
        mediator.Send(new GetTrustLedgerQuery(subjectType, subjectId, limit), cancellationToken);
}
