using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Market.Workspace;

public sealed class MarketWorkspaceRepository(AppDbContext db) : IMarketWorkspaceRepository
{
    public async Task<MarketWorkspaceState?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.MarketWorkspaces.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        return row.State;
    }

    public async Task SaveAsync(MarketWorkspaceState document, CancellationToken cancellationToken = default)
    {
        var row = await db.MarketWorkspaces.FirstOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.MarketWorkspaces.Add(new MarketWorkspaceRow
            {
                State = document,
                UpdatedAt = now,
            });
        }
        else
        {
            row.State = document;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
