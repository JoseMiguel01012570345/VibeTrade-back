using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Trust.Dtos;

namespace VibeTrade.Backend.Features.Trust.GetTrustLedger;

public sealed class GetTrustLedgerHandler(AppDbContext db)
    : IRequestHandler<GetTrustLedgerQuery, IReadOnlyList<TrustHistoryItemDto>>
{
    public async Task<IReadOnlyList<TrustHistoryItemDto>> Handle(
        GetTrustLedgerQuery request,
        CancellationToken cancellationToken)
    {
        var st = (request.SubjectType ?? "").Trim();
        var sid = (request.SubjectId ?? "").Trim();
        if (st.Length < 2 || sid.Length < 2)
            return [];

        var take = Math.Clamp(request.Limit, 1, 200);
        return await db.TrustScoreLedgerRows.AsNoTracking()
            .Where(x => x.SubjectType == st && x.SubjectId == sid)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new TrustHistoryItemDto(
                x.Id,
                x.CreatedAtUtc,
                x.Delta,
                x.BalanceAfter,
                x.Reason))
            .ToListAsync(cancellationToken);
    }
}
